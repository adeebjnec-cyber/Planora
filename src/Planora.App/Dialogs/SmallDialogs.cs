using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Planora.App.Infrastructure;
using Planora.App.ViewModels;
using Planora.Engine.Model;

namespace Planora.App.Dialogs;

/// <summary>Shared layout helpers for the small code-built dialogs.</summary>
internal static class Form
{
    public static TextBox Box(string text = "", double width = 140) => new() { Text = text, Width = width, Padding = new Thickness(3, 2, 3, 2), Margin = new Thickness(0, 3, 0, 3), HorizontalAlignment = HorizontalAlignment.Left };

    public static Grid Fields(params (string Label, UIElement Field)[] rows)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = rows[i].Label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetRow(label, i);
            Grid.SetRow(rows[i].Field, i);
            Grid.SetColumn(rows[i].Field, 1);
            grid.Children.Add(label);
            grid.Children.Add(rows[i].Field);
        }
        return grid;
    }

    public static StackPanel Buttons(Window window, Action onOk, string okText = "OK")
    {
        var ok = new Button { Content = okText, IsDefault = true, MinWidth = 80, Padding = new Thickness(10, 4, 10, 4) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
        ok.Click += (_, _) => onOk();
        return new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), Children = { ok, cancel } };
    }

    public static void Setup(Window window, string title)
    {
        window.Title = title;
        window.Owner = Application.Current.MainWindow;
        window.Style = (Style)Application.Current.Resources["DialogWindow"];
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }

    public static void Warn(Window window, string message) =>
        MessageBox.Show(window, message, "Planora", MessageBoxButton.OK, MessageBoxImage.Warning);
}

/// <summary>Split Task: interrupt a task after some of its work, or remove its splits.</summary>
public sealed class SplitTaskDialog : Window
{
    public SplitTaskDialog(MainViewModel owner, ProjectTask task)
    {
        Form.Setup(this, "Split Task");
        var o = owner.Project.Options;
        var after = Form.Box(DurationText.Format(task.DurationMinutes / 2, o));
        var gap = Form.Box(DurationText.Format(o.MinutesPerDay, o));
        var existing = task.Splits.Count == 0 ? "This task has no splits."
            : "Current splits: " + string.Join(", ", task.Splits.Select(s => $"after {DurationText.Format(s.AfterWorkMinutes, o)} pause {DurationText.Format(s.GapMinutes, o)}"));
        var remove = new Button { Content = "Remove All Splits", Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0), IsEnabled = task.Splits.Count > 0 };
        int id = task.Id;
        remove.Click += (_, _) =>
        {
            if (owner.TryEdit("Remove splits", p => p.GetRequiredTask(id).RemoveSplits()))
                DialogResult = true;
        };

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Width = 420,
            Children =
            {
                new TextBlock { Text = $"{task.Name} ({DurationText.Format(task.DurationMinutes, o)})", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) },
                Form.Fields(("Split after this much work:", after), ("Pause for:", gap)),
                new TextBlock { Text = existing, TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 8, 0, 0) },
                remove,
                new TextBlock { Text = "Tip: Task > Split Mode lets you click a bar to split it at that point.", Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 8, 0, 0) },
                Form.Buttons(this, () =>
                {
                    if (!DurationText.TryParse(after.Text, o, out var a) || !DurationText.TryParse(gap.Text, o, out var g))
                    {
                        Form.Warn(this, "Enter durations such as 2d or 4h.");
                        return;
                    }
                    if (a.Minutes <= 0 || a.Minutes >= task.DurationMinutes || g.Minutes <= 0)
                    {
                        Form.Warn(this, "The split must fall inside the task, and the pause must be longer than zero.");
                        return;
                    }
                    owner.SplitTask(task, a.Minutes, g.Minutes);
                    DialogResult = true;
                }),
            },
        };
    }
}

/// <summary>Assignment Information: units, work, contour, delay, rate table and cost of one assignment.</summary>
public sealed class AssignmentInformationDialog : Window
{
    public AssignmentInformationDialog(MainViewModel owner, Assignment assignment)
    {
        Form.Setup(this, "Assignment Information");
        var o = owner.Project.Options;
        bool work = assignment.IsWork, cost = assignment.Resource.Type == ResourceType.Cost;
        var units = Form.Box(work ? $"{assignment.Units * 100:0.##}%" : assignment.Units.ToString("0.##", CultureInfo.CurrentCulture));
        var workBox = Form.Box(DurationText.Format(assignment.WorkMinutes, o, DurationUnit.Hours));
        var overtime = Form.Box(DurationText.Format(assignment.OvertimeWorkMinutes, o, DurationUnit.Hours));
        var contour = new ComboBox { ItemsSource = Enum.GetNames<WorkContour>(), SelectedIndex = (int)assignment.Contour, Width = 140, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 3) };
        var delay = Form.Box(DurationText.Format(assignment.DelayMinutes, o));
        var table = new ComboBox { ItemsSource = new[] { "A", "B", "C", "D", "E" }, SelectedIndex = assignment.CostRateTable, Width = 60, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 3) };
        var amount = Form.Box(assignment.CostAmount.ToString("0.##", CultureInfo.CurrentCulture));
        units.IsEnabled = !cost;
        workBox.IsEnabled = overtime.IsEnabled = contour.IsEnabled = delay.IsEnabled = work;
        table.IsEnabled = !cost;
        amount.IsEnabled = cost;

        var (taskId, resourceId) = (assignment.Task.Id, assignment.Resource.Id);
        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Width = 380,
            Children =
            {
                new TextBlock { Text = $"{assignment.Resource.Name} on {assignment.Task.Name}", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) },
                Form.Fields(("Units:", units), ("Work:", workBox), ("Overtime work:", overtime), ("Work contour:", contour), ("Delay:", delay), ("Cost rate table:", table), ("Cost (cost resources):", amount)),
                new TextBlock { Text = $"Start {Infrastructure.DateText.Format(assignment.Start)}   Finish {Infrastructure.DateText.Format(assignment.Finish)}   Cost {assignment.Cost:C}", Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 8, 0, 0) },
                Form.Buttons(this, () =>
                {
                    bool ok = owner.TryEdit("Assignment information", p =>
                    {
                        var task = p.GetRequiredTask(taskId);
                        var a = task.Assignments.Single(x => x.Resource.Id == resourceId);
                        if (!cost)
                        {
                            if (!double.TryParse(units.Text.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.CurrentCulture, out var u) || u < 0)
                                throw new FormatException("Units must be a positive number.");
                            var newUnits = work ? u / 100 : u;
                            if (Math.Abs(newUnits - a.Units) > 1e-9)
                                p.SetUnits(a, newUnits);
                            a.CostRateTable = table.SelectedIndex;
                        }
                        if (work)
                        {
                            a.Contour = (WorkContour)contour.SelectedIndex;
                            if (!DurationText.TryParse(workBox.Text, p.Options, out var w, DurationUnit.Hours))
                                throw new FormatException($"'{workBox.Text}' is not a valid amount of work.");
                            if (w.Minutes != a.WorkMinutes)
                                p.SetWork(a, w.Minutes);
                            if (!DurationText.TryParse(overtime.Text, p.Options, out var ot, DurationUnit.Hours) || ot.Minutes > a.WorkMinutes)
                                throw new FormatException("Overtime must be an amount of work no larger than the total work.");
                            a.OvertimeWorkMinutes = ot.Minutes;                            if (!DurationText.TryParse(delay.Text, p.Options, out var d))
                                throw new FormatException($"'{delay.Text}' is not a valid delay.");
                            a.DelayMinutes = d.Minutes;
                        }
                        if (cost)
                        {
                            if (!decimal.TryParse(amount.Text, NumberStyles.Currency, CultureInfo.CurrentCulture, out var c) || c < 0)
                                throw new FormatException("Cost must be a positive amount.");
                            a.CostAmount = c;
                        }
                    });
                    if (ok)
                        DialogResult = true;
                }),
            },
        };
    }
}

/// <summary>Project budgets: amounts for budget resources (cost budgets, or work/material quantities).</summary>
public sealed class BudgetDialog : Window
{
    public sealed class Row
    {
        public string Resource { get; set; } = "";
        public string Type { get; set; } = "Cost";
        public string Amount { get; set; } = "0";
    }

    public BudgetDialog(MainViewModel owner)
    {
        Form.Setup(this, "Budgets");
        var project = owner.Project;
        var rows = new ObservableCollection<Row>(project.Resources.Where(r => r.IsBudget).Select(r => new Row
        {
            Resource = r.Name,
            Type = r.Type.ToString(),
            Amount = r.Type == ResourceType.Cost
                ? (project.BudgetCosts.TryGetValue(r, out var c) ? c : 0).ToString("0.##", CultureInfo.CurrentCulture)
                : (project.BudgetWork.TryGetValue(r, out var w) ? w : 0).ToString("0.##", CultureInfo.CurrentCulture),
        }));
        var grid = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, CanUserAddRows = true, CanUserDeleteRows = true, Height = 220, Width = 460, RowHeight = 24, HeadersVisibility = DataGridHeadersVisibility.All };
        grid.Columns.Add(new DataGridTextColumn { Header = "Budget Resource", Binding = new System.Windows.Data.Binding(nameof(Row.Resource)), Width = 200 });
        grid.Columns.Add(new DataGridComboBoxColumn { Header = "Type", ItemsSource = new[] { "Cost", "Work", "Material" }, SelectedItemBinding = new System.Windows.Data.Binding(nameof(Row.Type)), Width = 90 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Budget (amount, hours or units)", Binding = new System.Windows.Data.Binding(nameof(Row.Amount)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Children =
            {
                new TextBlock { Text = "Budgets are held by budget resources at project level, as in Microsoft Project. Compare them with costs in the Cost Overview report.", TextWrapping = TextWrapping.Wrap, Width = 460, Margin = new Thickness(0, 0, 0, 8) },
                grid,
                Form.Buttons(this, () =>
                {
                    grid.CommitEdit(DataGridEditingUnit.Row, true);
                    bool ok = owner.TryEdit("Budgets", p =>
                    {
                        p.BudgetCosts.Clear();
                        p.BudgetWork.Clear();
                        foreach (var row in rows.Where(r => !string.IsNullOrWhiteSpace(r.Resource)))
                        {
                            var type = Enum.Parse<ResourceType>(row.Type);
                            var resource = p.Resources.FirstOrDefault(r => r.IsBudget && r.Name.Equals(row.Resource.Trim(), StringComparison.OrdinalIgnoreCase))
                                ?? p.AddResource(row.Resource.Trim(), type);
                            resource.IsBudget = true;
                            resource.Type = type;
                            if (!decimal.TryParse(row.Amount, NumberStyles.Currency, CultureInfo.CurrentCulture, out var amount) || amount < 0)
                                throw new FormatException($"'{row.Amount}' is not a valid budget for {row.Resource}.");
                            if (type == ResourceType.Cost)
                                p.BudgetCosts[resource] = amount;
                            else
                                p.BudgetWork[resource] = (double)amount;
                        }
                    });
                    if (ok)
                        DialogResult = true;
                }),
            },
        };
    }
}

/// <summary>Options: defaults for new tasks and autosave interval.</summary>
public sealed class OptionsDialog : Window
{
    public OptionsDialog(MainViewModel owner)
    {
        Form.Setup(this, "Options");
        var o = owner.Project.Options;
        var taskType = new ComboBox { ItemsSource = new[] { "Fixed Units", "Fixed Duration", "Fixed Work" }, SelectedIndex = (int)o.DefaultTaskType, Width = 160, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 3) };
        var effort = new CheckBox { Content = "New tasks are effort driven", IsChecked = o.NewTasksEffortDriven, Margin = new Thickness(0, 6, 0, 0) };
        var manual = new CheckBox { Content = "New tasks are manually scheduled", IsChecked = o.NewTasksManuallyScheduled, Margin = new Thickness(0, 6, 0, 0) };
        var autosave = Form.Box(owner.AutosaveMinutes.ToString(CultureInfo.CurrentCulture), 60);
        var theme = new ComboBox { ItemsSource = Themes.Names, SelectedItem = Themes.Names.Contains(owner.Theme) ? owner.Theme : Themes.Default, Width = 160, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 3) };

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Width = 380,
            Children =
            {
                new TextBlock { Text = "Schedule options for this project", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) },
                Form.Fields(("Default task type:", taskType)),
                effort,
                manual,
                new TextBlock { Text = "Hours per day, critical slack and status-date rules are under Project > Project Information.", TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 8, 0, 0) },
                new TextBlock { Text = "Planora", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6) },
                Form.Fields(("Autosave every (minutes, 0 = off):", autosave), ("Office theme:", theme)),
                Form.Buttons(this, () =>
                {
                    if (!int.TryParse(autosave.Text, out var minutes) || minutes is < 0 or > 120)
                    {
                        Form.Warn(this, "Autosave minutes must be from 0 to 120.");
                        return;
                    }
                    owner.AutosaveMinutes = minutes;
                    owner.Theme = theme.SelectedItem as string ?? Themes.Default;
                    bool ok = owner.TryEdit("Options", p =>
                    {
                        p.Options.DefaultTaskType = (TaskType)taskType.SelectedIndex;
                        p.Options.NewTasksEffortDriven = effort.IsChecked == true;
                        p.Options.NewTasksManuallyScheduled = manual.IsChecked == true;
                    });
                    if (ok)
                        DialogResult = true;
                }),
            },
        };
    }
}
