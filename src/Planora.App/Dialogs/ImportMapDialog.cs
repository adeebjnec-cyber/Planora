using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Planora.Engine.Fields;
using Planora.Engine.Interop;

namespace Planora.App.Dialogs;

/// <summary>
/// Import Wizard for Excel and CSV files (Microsoft Project's "Task Mapping"): choose which Planora field
/// each spreadsheet column goes into, with sample values from the file. Columns mapped to custom fields
/// can take the column's name as the field's alias.
/// </summary>
public sealed class ImportMapDialog : Window
{
    private readonly IReadOnlyList<string[]> _rows;
    private readonly ObservableCollection<MapRow> _map = new();
    private readonly CheckBox _hasHeader = new() { Content = "The first row contains column names", IsChecked = true, Margin = new Thickness(0, 0, 0, 8) };
    private readonly DataGrid _grid;

    public ImportMapDialog(IReadOnlyList<string[]> rows, string fileName)
    {
        _rows = rows;
        Form.Setup(this, $"Import Wizard - {fileName}");
        var targets = new List<KeyChoice> { new("", "(do not import)") };
        targets.AddRange(SpreadsheetFile.ImportTargets.Select(t => new KeyChoice(t, t)));
        targets.AddRange(CustomField.AllSlots().Select(k => new KeyChoice(k, $"{k} (custom field)")));

        _grid = new DataGrid
        {
            ItemsSource = _map,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeight = 26,
            Height = 330,
        };
        _grid.Columns.Add(new DataGridTextColumn { Header = "From: column", Binding = new Binding(nameof(MapRow.Source)), IsReadOnly = true, Width = 160 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Sample values", Binding = new Binding(nameof(MapRow.Sample)), IsReadOnly = true, Width = 250 });
        _grid.Columns.Add(new DataGridComboBoxColumn
        {
            Header = "To: Planora field",
            Width = 200,
            ItemsSource = targets,
            DisplayMemberPath = nameof(KeyChoice.Name),
            SelectedValuePath = nameof(KeyChoice.Key),
            SelectedValueBinding = new Binding(nameof(MapRow.Target)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
        });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Custom field name", Binding = new Binding(nameof(MapRow.Alias)), Width = 150 });

        _hasHeader.Checked += (_, _) => BuildMap();
        _hasHeader.Unchecked += (_, _) => BuildMap();
        BuildMap();

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Width = 780,
            Children =
            {
                new TextBlock
                {
                    Text = $"{Math.Max(0, rows.Count - 1)} data rows. Choose where each column goes; the columns Planora recognised are already mapped. One column must go to Task Name.",
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
                },
                _hasHeader,
                _grid,
                Form.Buttons(this, Accept, "Import"),
            },
        };
    }

    /// <summary>Rows ready for <see cref="SpreadsheetFile.Import"/> (after OK).</summary>
    public List<string[]> MappedRows { get; private set; } = new();

    /// <summary>Aliases for the custom fields columns were mapped to.</summary>
    public Dictionary<string, string> Aliases { get; } = new();

    private void BuildMap()
    {
        _map.Clear();
        bool header = _hasHeader.IsChecked == true;
        int columns = _rows.Count == 0 ? 0 : _rows.Max(r => r.Length);
        var data = _rows.Skip(header ? 1 : 0).Take(4).ToList();
        var guesses = Enumerable.Range(0, columns).Select(c => header && c < _rows[0].Length ? SpreadsheetFile.DefaultTarget(_rows[0][c]) : null).ToList();
        // Columns Planora does not recognise keep their data in the next free Text field.
        int nextText = 1;
        string? NextFreeText()
        {
            while (nextText <= 30 && guesses.Contains($"Text{nextText}"))
                nextText++;
            return nextText <= 30 ? $"Text{nextText++}" : null;
        }
        for (int c = 0; c < columns; c++)
        {
            string name = header && c < _rows[0].Length && _rows[0][c].Trim().Length > 0 ? _rows[0][c].Trim() : $"Column {c + 1}";
            string sample = string.Join(" | ", data.Select(r => c < r.Length ? r[c].Trim() : "").Where(v => v.Length > 0));
            string? target = guesses[c] ?? (header && sample.Length > 0 ? NextFreeText() : null);
            _map.Add(new MapRow { Source = name, Sample = sample, Target = target ?? "", Alias = guesses[c] is null ? name : "" });
        }
        // Without names, guess the first text column is the task name.
        if (!header && _map.Count > 0 && _map.All(m => m.Target.Length == 0))
            _map[0].Target = "Task Name";
        _grid.Items.Refresh();
    }

    private void Accept()
    {
        _grid.CommitEdit(DataGridEditingUnit.Row, true);
        var targets = _map.Select(m => m.Target.Length == 0 ? null : m.Target).ToList();
        if (!targets.Contains("Task Name"))
        {
            Form.Warn(this, "Map one column to Task Name.");
            return;
        }
        var duplicate = targets.Where(t => t is not null).GroupBy(t => t).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            Form.Warn(this, $"Two columns go to {duplicate.Key}. Each field can take one column.");
            return;
        }
        MappedRows = SpreadsheetFile.ApplyMap(_rows, targets, _hasHeader.IsChecked == true);
        foreach (var row in _map.Where(m => CustomField.TypeOfKey(m.Target) is not null && m.Alias.Trim().Length > 0))
            Aliases[row.Target] = row.Alias.Trim();
        DialogResult = true;
    }

    public sealed class MapRow
    {
        public string Source { get; init; } = "";
        public string Sample { get; init; } = "";
        public string Target { get; set; } = "";
        public string Alias { get; set; } = "";
    }
}
