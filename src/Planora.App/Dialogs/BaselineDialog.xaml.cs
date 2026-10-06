using System.Globalization;
using System.Windows;
using Planora.App.ViewModels;
using Planora.Engine.Model;

namespace Planora.App.Dialogs;

/// <summary>Set Baseline / Clear Baseline: baselines 0-10 and interim plans 1-10, for the whole project or selected tasks.</summary>
public partial class BaselineDialog : Window
{
    private readonly MainViewModel _owner;
    private readonly bool _clear;

    public BaselineDialog(MainViewModel owner, bool clear)
    {
        _owner = owner;
        _clear = clear;
        InitializeComponent();

        var project = owner.Project;
        for (int i = 0; i < Baseline.Count; i++)
        {
            var name = BaselineName(i);
            var saved = project.Baselines[i]?.SavedOn ?? project.Tasks.Select(t => t.Baselines[i]?.SavedOn).FirstOrDefault(d => d is not null);
            BaselinePicker.Items.Add(saved is { } on ? $"{name} (last saved on {on.ToString("g", CultureInfo.CurrentCulture)})" : name);
        }
        BaselinePicker.SelectedIndex = 0;

        CopyFrom.Items.Add("Start/Finish");
        for (int i = 0; i < Baseline.Count; i++)
            CopyFrom.Items.Add(BaselineName(i));
        CopyFrom.SelectedIndex = 0;
        for (int i = 1; i <= Baseline.InterimPlanCount; i++)
            CopyInto.Items.Add($"Start{i}/Finish{i}");
        CopyInto.SelectedIndex = 0;

        if (clear)
        {
            Title = "Clear Baseline";
            BaselineMode.Content = "Clear baseline plan";
            InterimMode.Content = "Clear interim plan";
            CopyLabel.Visibility = CopyFrom.Visibility = Visibility.Collapsed;
            RollUp.Visibility = Visibility.Collapsed;
        }
        SelectedTasks.IsEnabled = owner.SelectedTasks.Count > 0;
        OnModeChanged(this, new RoutedEventArgs());
    }

    private static string BaselineName(int index) => index == 0 ? "Baseline" : $"Baseline {index}";

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (BaselinePicker is null || InterimPanel is null)
            return;
        BaselinePicker.IsEnabled = BaselineMode.IsChecked == true;
        InterimPanel.IsEnabled = InterimMode.IsChecked == true;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var taskIds = SelectedTasks.IsChecked == true ? _owner.SelectedTasks.Select(t => t.Id).ToList() : null;
        int baseline = BaselinePicker.SelectedIndex;
        int interim = CopyInto.SelectedIndex;
        int? fromBaseline = CopyFrom.SelectedIndex == 0 ? null : CopyFrom.SelectedIndex - 1;
        bool rollUp = RollUp.IsChecked == true;
        bool isBaseline = BaselineMode.IsChecked == true;

        if (!_clear && isBaseline && taskIds is null && _owner.Project.Baselines[baseline] is not null &&
            MessageBox.Show(this, $"{BaselineName(baseline)} has already been saved. Overwrite it?", "Planora",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        string description = (_clear ? "Clear " : "Set ") + (isBaseline ? BaselineName(baseline).ToLowerInvariant() : "interim plan");
        bool ok = _owner.TryEdit(description, p =>
        {
            var tasks = taskIds?.Select(p.GetRequiredTask).ToList();
            if (isBaseline && _clear)
                p.ClearBaseline(baseline, tasks);
            else if (isBaseline)
                p.SaveBaseline(baseline, tasks, rollUp);
            else if (_clear)
            {
                foreach (var t in tasks ?? p.Tasks.ToList())
                    t.InterimPlans[interim] = null;
            }
            else
                p.SaveInterimPlan(interim, tasks, fromBaseline);
        });
        if (ok)
            DialogResult = true;
    }
}
