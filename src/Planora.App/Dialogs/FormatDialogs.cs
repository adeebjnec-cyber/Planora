using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Planora.App.ViewModels;
using Planora.Engine.Fields;
using Planora.Engine.Views;

namespace Planora.App.Dialogs;

/// <summary>Colour choices for the formatting dialogs.</summary>
public sealed record ColorChoice(string Name, string Hex)
{
    public static readonly IReadOnlyList<ColorChoice> Palette = new ColorChoice[]
    {
        new("Automatic", ""), new("Navy", "#1B2333"), new("Slate", "#2A3242"), new("Gray", "#8A93A2"), new("Light Gray", "#C3C8D1"),
        new("White", "#FFFFFF"), new("Indigo", "#8F9AEA"), new("Dark Indigo", "#4152CC"), new("Blue", "#7FA6E3"), new("Dark Blue", "#2F5D9E"),
        new("Red", "#E2A39C"), new("Dark Red", "#BC4B42"), new("Teal", "#9FD0D6"), new("Dark Teal", "#1F7A7A"), new("Green", "#A9D5BC"),
        new("Dark Green", "#3E8E68"), new("Amber", "#EBC98A"), new("Dark Amber", "#B7832F"), new("Purple", "#C2B5E6"), new("Dark Purple", "#6B4FB0"),
    };

    /// <summary>The palette plus a colour that is not in it (so existing values still show).</summary>
    public static IReadOnlyList<ColorChoice> With(params string?[] extra)
    {
        var list = Palette.ToList();
        foreach (var hex in extra)
        {
            if (!string.IsNullOrEmpty(hex) && list.All(c => !c.Hex.Equals(hex, StringComparison.OrdinalIgnoreCase)))
                list.Add(new ColorChoice(hex, hex));
        }
        return list;
    }
}

/// <summary>A field choice for bar texts ("" = none).</summary>
public sealed record KeyChoice(string Key, string Name);

/// <summary>
/// Bar Styles: one row per style: what it applies to (task kind, optionally only tasks with a flag field
/// set), colours, shape and the texts around the bar. Later rows win over earlier ones.
/// </summary>
public sealed class BarStylesDialog : Window
{
    private readonly MainViewModel _owner;
    private readonly ObservableCollection<BarStyle> _styles;
    private readonly DataGrid _grid;

    public BarStylesDialog(MainViewModel owner)
    {
        _owner = owner;
        Form.Setup(this, "Bar Styles");
        _styles = new ObservableCollection<BarStyle>(owner.Project.GanttFormat.BarStyles.Select(s => s.Copy()));

        var project = owner.Project;
        var flags = new List<KeyChoice> { new("", "(all tasks)") };
        flags.AddRange(TaskFields.All(project).Where(f => f.Type == FieldType.Flag).Select(f => new KeyChoice(f.Key, f.Name)));
        var texts = new List<KeyChoice> { new("", "") };
        texts.AddRange(TaskFields.All(project).Select(f => new KeyChoice(f.Key, f.Name)));
        var colors = ColorChoice.With(_styles.SelectMany(s => new[] { s.Fill, s.Border }).ToArray());

        _grid = new DataGrid
        {
            ItemsSource = _styles,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeight = 26,
            Height = 300,
            SelectionMode = DataGridSelectionMode.Single,
        };
        _grid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding(nameof(BarStyle.Name)), Width = 120 });
        _grid.Columns.Add(Combo("Show for", nameof(BarStyle.Kind), Enum.GetValues<BarKind>().Select(k => new KeyChoice(k.ToString(), Label(k))).ToList(), 110, enumType: typeof(BarKind)));
        _grid.Columns.Add(Combo("Only when", nameof(BarStyle.FlagField), flags, 110, nullable: true));
        _grid.Columns.Add(Combo("Fill", nameof(BarStyle.Fill), colors.Select(c => new KeyChoice(c.Hex, c.Name)).ToList(), 90));
        _grid.Columns.Add(Combo("Border", nameof(BarStyle.Border), colors.Select(c => new KeyChoice(c.Hex, c.Name)).ToList(), 90));
        _grid.Columns.Add(Combo("Shape", nameof(BarStyle.Shape), Enum.GetValues<BarShape>().Select(s => new KeyChoice(s.ToString(), s.ToString())).ToList(), 70, enumType: typeof(BarShape)));
        _grid.Columns.Add(Combo("Left text", nameof(BarStyle.LeftText), texts, 100, nullable: true));
        _grid.Columns.Add(Combo("Right text", nameof(BarStyle.RightText), texts, 110, nullable: true));
        _grid.Columns.Add(Combo("Top text", nameof(BarStyle.TopText), texts, 100, nullable: true));
        _grid.Columns.Add(Combo("Inside text", nameof(BarStyle.InsideText), texts, 100, nullable: true));

        var add = Small("Insert Row", () =>
        {
            var style = new BarStyle { Name = "Marked", Kind = BarKind.Task, FlagField = "Marked", Fill = "#FFE680", Border = "#C09000", RightText = "ResourceNames" };
            _styles.Insert(Math.Max(0, _grid.SelectedIndex + 1 > 0 ? _grid.SelectedIndex + 1 : _styles.Count), style);
            _grid.SelectedItem = style;
        });
        var remove = Small("Delete Row", () =>
        {
            if (_grid.SelectedItem is BarStyle style)
                _styles.Remove(style);
        });
        var reset = Small("Reset to Defaults", () =>
        {
            _styles.Clear();
            foreach (var style in GanttFormat.DefaultBarStyles())
                _styles.Add(style);
        });

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Width = 1060,
            Children =
            {
                new TextBlock
                {
                    Text = "Each row draws one kind of bar. A row with 'Only when' set applies just to tasks with that Yes/No field set (e.g. Marked or Flag1) and wins over the rows above it.",
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
                },
                _grid,
                new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0), Children = { add, remove, reset } },
                Form.Buttons(this, Apply),
            },
        };
    }

    private static string Label(BarKind kind) => kind switch
    {
        BarKind.CriticalTask => "Critical task",
        BarKind.ManualTask => "Manual task",
        BarKind.InactiveTask => "Inactive task",
        _ => kind.ToString(),
    };

    private static Button Small(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>A combo-box column storing the choice's key (as text, an enum, or null for "").</summary>
    internal static DataGridComboBoxColumn Combo(string header, string path, IReadOnlyList<KeyChoice> choices, double width, bool nullable = false, Type? enumType = null) => new()
    {
        Header = header,
        Width = width,
        ItemsSource = choices,
        DisplayMemberPath = nameof(KeyChoice.Name),
        SelectedValuePath = nameof(KeyChoice.Key),
        SelectedValueBinding = new Binding(path) { Converter = new KeyConverter(nullable, enumType), UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
    };

    private sealed class KeyConverter : IValueConverter
    {
        private readonly bool _nullable;
        private readonly Type? _enumType;

        public KeyConverter(bool nullable, Type? enumType) => (_nullable, _enumType) = (nullable, enumType);

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value?.ToString() ?? "";

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var text = value as string ?? "";
            if (_enumType is not null)
                return Enum.Parse(_enumType, text);
            return _nullable && text.Length == 0 ? null : text;
        }
    }

    private void Apply()
    {
        _grid.CommitEdit(DataGridEditingUnit.Row, true);
        var styles = _styles.Select(s => s.Copy()).ToList();
        if (_owner.TryEdit("Bar styles", p => p.GanttFormat.BarStyles = styles))
            DialogResult = true;
    }
}

/// <summary>Text Styles: bold, italic, colour and size for bar text and for kinds of task rows.</summary>
public sealed class TextStylesDialog : Window
{
    private readonly MainViewModel _owner;
    private readonly List<TextStyleRow> _rows;
    private readonly DataGrid _grid;

    public TextStylesDialog(MainViewModel owner)
    {
        _owner = owner;
        Form.Setup(this, "Text Styles");
        var format = owner.Project.GanttFormat;
        _rows = Enum.GetValues<TextItem>().Select(item => new TextStyleRow(format.Text(item))).ToList();
        var colors = ColorChoice.With(_rows.Select(r => r.Color).ToArray()).Select(c => new KeyChoice(c.Hex, c.Name)).ToList();

        _grid = new DataGrid
        {
            ItemsSource = _rows,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeight = 26,
        };
        _grid.Columns.Add(new DataGridTextColumn { Header = "Item to change", Binding = new Binding(nameof(TextStyleRow.Name)), IsReadOnly = true, Width = 150 });
        _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Bold", Binding = new Binding(nameof(TextStyleRow.Bold)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 50 });
        _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Italic", Binding = new Binding(nameof(TextStyleRow.Italic)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 50 });
        _grid.Columns.Add(BarStylesDialog.Combo("Color", nameof(TextStyleRow.Color), colors, 110, nullable: true));
        _grid.Columns.Add(new DataGridTextColumn { Header = "Size (pt)", Binding = new Binding(nameof(TextStyleRow.Size)), Width = 70 });

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Width = 460,
            Children =
            {
                new TextBlock { Text = "Leave Size empty to keep the normal size. Row styles apply in the task sheet; Bar Text applies to the texts beside Gantt bars.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) },
                _grid,
                Form.Buttons(this, Apply),
            },
        };
    }

    private void Apply()
    {
        _grid.CommitEdit(DataGridEditingUnit.Row, true);
        var styles = new List<TextStyle>();
        foreach (var row in _rows)
        {
            double? size = null;
            if (!string.IsNullOrWhiteSpace(row.Size))
            {
                if (!double.TryParse(row.Size, NumberStyles.Float, CultureInfo.CurrentCulture, out var s) || s is < 6 or > 36)
                {
                    Form.Warn(this, $"{row.Name}: the size must be a number of points from 6 to 36.");
                    return;
                }
                size = s;
            }
            styles.Add(new TextStyle { Item = row.Item, Bold = row.Bold, Italic = row.Italic, Color = string.IsNullOrEmpty(row.Color) ? null : row.Color, Size = size });
        }
        if (_owner.TryEdit("Text styles", p => p.GanttFormat.TextStyles = styles))
            DialogResult = true;
    }

    public sealed class TextStyleRow
    {
        public TextStyleRow(TextStyle style)
        {
            Item = style.Item;
            Bold = style.Bold;
            Italic = style.Italic;
            Color = style.Color;
            Size = style.Size?.ToString("0.#", CultureInfo.CurrentCulture) ?? "";
        }

        public TextItem Item { get; }

        public string Name => Item switch
        {
            TextItem.BarText => "Bar Text",
            TextItem.CriticalTasks => "Critical Tasks",
            TextItem.SummaryTasks => "Summary Tasks",
            TextItem.MilestoneTasks => "Milestone Tasks",
            TextItem.MarkedTasks => "Marked Tasks",
            _ => "External / Subproject Tasks",
        };

        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public string? Color { get; set; }
        public string Size { get; set; }
    }
}

/// <summary>Timescale: automatic (follows the zoom) or one to three tiers with unit, count and label.</summary>
public sealed class TimescaleDialog : Window
{
    private static readonly (string Label, string Format)[] LabelPresets =
    {
        ("2026", "yyyy"), ("H1 2026", "'H'{H} yyyy"), ("Q1 2026", "'Q'{Q} yyyy"), ("Q1", "'Q'{Q}"),
        ("January 2026", "MMMM yyyy"), ("Jan '26", "MMM ''yy"), ("Jan", "MMM"), ("J", "MMMMM"),
        ("25 Jan '26", "d MMM ''yy"), ("25 Jan", "d MMM"), ("Mon 25", "ddd d"), ("25", "%d"), ("Mon", "ddd"), ("M", "dddd"),
        ("08:00", "HH:mm"), ("8", "%H"),
    };

    private readonly MainViewModel _owner;
    private readonly CheckBox _automatic = new() { Content = "Automatic (tiers follow the zoom level)" };
    private readonly ComboBox _tierCount = new() { Width = 120, ItemsSource = new[] { "One tier", "Two tiers", "Three tiers" } };
    private readonly CheckBox _shade = new() { Content = "Shade non-working time", Margin = new Thickness(0, 8, 0, 0) };
    private readonly List<(ComboBox Unit, TextBox Count, ComboBox Label, StackPanel Row)> _tiers = new();

    public TimescaleDialog(MainViewModel owner)
    {
        _owner = owner;
        Form.Setup(this, "Timescale");
        var settings = owner.Project.GanttFormat.Timescale;
        _automatic.IsChecked = settings.Automatic;
        _tierCount.SelectedIndex = Math.Clamp(settings.TierCount, 1, 3) - 1;
        _shade.IsChecked = settings.ShadeNonWorkingTime;

        var panel = new StackPanel { Margin = new Thickness(14), Width = 520 };
        panel.Children.Add(_automatic);
        panel.Children.Add(Rows.Row(Rows.Label("Show:", 60), _tierCount));
        string[] names = { "Top tier", "Middle tier", "Bottom tier" };
        for (int i = 0; i < 3; i++)
        {
            var tier = i < settings.Tiers.Count ? settings.Tiers[i] : new TimescaleTierSetting();
            var unit = new ComboBox { Width = 120, ItemsSource = Enum.GetValues<TimescaleUnit>().Select(UnitName).ToList(), SelectedIndex = (int)tier.Unit, Margin = new Thickness(0, 2, 8, 2) };
            var count = Rows.Box(40);
            count.Text = tier.Count.ToString(CultureInfo.CurrentCulture);
            var label = new ComboBox { Width = 150, IsEditable = true, ItemsSource = LabelPresets.Select(p => p.Format).ToList(), Text = tier.Label, Margin = new Thickness(0, 2, 0, 2) };
            label.ToolTip = "A date format, e.g. MMM yyyy. {Q} is the quarter number, {H} the half-year.";
            var row = Rows.Row(Rows.Label(names[i] + ":", 80), Rows.Label("Units"), unit, Rows.Label("Count"), count, Rows.Label("Label"), label);
            _tiers.Add((unit, count, label, row));
            panel.Children.Add(row);
        }
        var preview = new TextBlock { Margin = new Thickness(0, 6, 0, 0), Foreground = System.Windows.Media.Brushes.DimGray };
        panel.Children.Add(preview);
        panel.Children.Add(_shade);
        panel.Children.Add(Form.Buttons(this, Apply));
        Content = panel;

        void Update()
        {
            bool manual = _automatic.IsChecked != true;
            _tierCount.IsEnabled = manual;
            for (int i = 0; i < 3; i++)
                _tiers[i].Row.IsEnabled = manual && i < _tierCount.SelectedIndex + 1;
            var sample = new TimescaleTierSetting { Label = _tiers[^1].Label.Text };
            preview.Text = $"Label example: {sample.Format(new DateTime(2026, 1, 26))}";
        }
        _automatic.Checked += (_, _) => Update();
        _automatic.Unchecked += (_, _) => Update();
        _tierCount.SelectionChanged += (_, _) => Update();
        foreach (var t in _tiers)
            t.Label.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Update()));
        Update();
    }

    private static string UnitName(TimescaleUnit unit) => unit switch
    {
        TimescaleUnit.ThirdsOfMonths => "Thirds of months",
        TimescaleUnit.HalfYears => "Half years",
        _ => unit.ToString(),
    };

    private void Apply()
    {
        var settings = new TimescaleSettings
        {
            Automatic = _automatic.IsChecked == true,
            TierCount = _tierCount.SelectedIndex + 1,
            ShadeNonWorkingTime = _shade.IsChecked == true,
            Tiers = new List<TimescaleTierSetting>(),
        };
        foreach (var (unit, count, label, _) in _tiers)
        {
            if (!int.TryParse(count.Text, out var n) || n is < 1 or > 100)
            {
                Form.Warn(this, "Each tier's count must be a whole number from 1 to 100.");
                return;
            }
            settings.Tiers.Add(new TimescaleTierSetting { Unit = (TimescaleUnit)unit.SelectedIndex, Count = n, Label = string.IsNullOrWhiteSpace(label.Text) ? "%d" : label.Text });
        }
        if (_owner.TryEdit("Timescale", p => p.GanttFormat.Timescale = settings))
            DialogResult = true;
    }
}

/// <summary>Gridlines: row and column lines, and which date lines to draw.</summary>
public sealed class GridlinesDialog : Window
{
    private readonly MainViewModel _owner;
    private readonly CheckBox _rows = new() { Content = "Gantt rows" }, _top = new() { Content = "Top tier column lines" }, _bottom = new() { Content = "Bottom tier column lines" };
    private readonly CheckBox _current = new() { Content = "Current date" }, _status = new() { Content = "Status date" }, _start = new() { Content = "Project start" }, _finish = new() { Content = "Project finish" };
    private readonly ComboBox _color = new() { Width = 140, DisplayMemberPath = nameof(ColorChoice.Name), SelectedValuePath = nameof(ColorChoice.Hex) };

    public GridlinesDialog(MainViewModel owner)
    {
        _owner = owner;
        Form.Setup(this, "Gridlines");
        var g = owner.Project.GanttFormat.Gridlines;
        (_rows.IsChecked, _top.IsChecked, _bottom.IsChecked) = (g.RowLines, g.TopTierColumns, g.BottomTierColumns);
        (_current.IsChecked, _status.IsChecked, _start.IsChecked, _finish.IsChecked) = (g.CurrentDate, g.StatusDate, g.ProjectStart, g.ProjectFinish);
        _color.ItemsSource = ColorChoice.With(g.Color).Where(c => c.Hex.Length > 0).ToList();
        _color.SelectedValue = g.Color;

        var panel = new StackPanel { Margin = new Thickness(14), Width = 320 };
        panel.Children.Add(Rows.Heading("Lines"));
        foreach (var box in new[] { _rows, _top, _bottom })
            panel.Children.Add(Spaced(box));
        panel.Children.Add(Rows.Row(Rows.Label("Line color:"), _color));
        panel.Children.Add(Rows.Heading("Date lines"));
        foreach (var box in new[] { _current, _status, _start, _finish })
            panel.Children.Add(Spaced(box));
        panel.Children.Add(Form.Buttons(this, Apply));
        Content = panel;
    }

    private static CheckBox Spaced(CheckBox box)
    {
        box.Margin = new Thickness(0, 2, 0, 2);
        return box;
    }

    private void Apply()
    {
        var settings = new GridlineSettings
        {
            RowLines = _rows.IsChecked == true,
            TopTierColumns = _top.IsChecked == true,
            BottomTierColumns = _bottom.IsChecked == true,
            CurrentDate = _current.IsChecked == true,
            StatusDate = _status.IsChecked == true,
            ProjectStart = _start.IsChecked == true,
            ProjectFinish = _finish.IsChecked == true,
            Color = _color.SelectedValue as string ?? "#D8D8D8",
        };
        if (_owner.TryEdit("Gridlines", p => p.GanttFormat.Gridlines = settings))
            DialogResult = true;
    }
}
