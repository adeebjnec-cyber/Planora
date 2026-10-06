using System.Windows;
using Planora.App.Infrastructure;
using Planora.App.ViewModels;
using Planora.Engine.Model;
using Planora.Engine.Scheduling;

namespace Planora.App.Dialogs;

/// <summary>Resource Leveling options, Level All and Clear Leveling.</summary>
public partial class LevelingDialog : Window
{
    private readonly MainViewModel _owner;

    public LevelingDialog(MainViewModel owner)
    {
        _owner = owner;
        InitializeComponent();
        RangeFrom.Text = DateText.Format(owner.Project.Start);
        RangeTo.Text = DateText.Format(owner.Project.Finish);
        _resources = owner.Project.Resources
            .Where(r => r.Type == ResourceType.Work && !r.IsBudget)
            .Select(r => new ResourceChoice(r.Id, r.Name, r.CanLevel && r.BookingType == BookingType.Committed))
            .ToList();
        ResourceList.ItemsSource = _resources;
    }

    private readonly List<ResourceChoice> _resources;

    public sealed class ResourceChoice
    {
        public ResourceChoice(int id, string name, bool enabled)
        {
            Id = id;
            Name = name;
            Enabled = enabled;
            Selected = enabled;
        }

        public int Id { get; }
        public string Name { get; }
        public bool Enabled { get; }
        public bool Selected { get; set; }
    }

    private void OnLevel(object sender, RoutedEventArgs e)
    {
        var cal = _owner.Project.Calendar;
        var options = new LevelingOptions
        {
            Order = (LevelingOrder)Order.SelectedIndex,
            LevelWithinAvailableSlack = WithinSlack.IsChecked == true,
            ClearLevelingValuesFirst = ClearFirst.IsChecked == true,
            Basis = (OverallocationBasis)Basis.SelectedIndex,
            CanSplitRemainingWork = CanSplit.IsChecked == true,
            LevelIndividualAssignments = Individual.IsChecked == true,
        };
        if (Range.IsChecked == true)
        {
            if (!DateText.TryParse(RangeFrom.Text, cal, false, out var from) || !DateText.TryParse(RangeTo.Text, cal, true, out var to))
            {
                MessageBox.Show(this, "Enter valid dates for the leveling range.", "Planora", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            options.From = from;
            options.To = to;
        }

        LevelingResult? result = null;
        bool allResources = _resources.All(r => r.Selected || !r.Enabled);
        var chosen = _resources.Where(r => r.Selected).Select(r => r.Id).ToHashSet();
        if (!_owner.TryEdit("Level resources", p =>
        {
            if (!allResources)
                options.Resources = p.Resources.Where(r => chosen.Contains(r.Id)).ToList();
            result = ResourceLeveler.Level(p, options);
        }))
            return;

        var message = $"Leveling delayed {result!.DelayedTasks.Count} task(s).";
        if (result.Unresolved.Count > 0)
        {
            message += $"\n\n{result.Unresolved.Count} overallocation(s) could not be resolved, for example "
                + $"{result.Unresolved[0].Resource.Name} on {DateText.Format(result.Unresolved[0].From)} "
                + "(a single assignment above the resource's maximum units, or tasks that cannot be moved).";
        }
        MessageBox.Show(this, message, "Resource Leveling", MessageBoxButton.OK, MessageBoxImage.Information);
        DialogResult = true;
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (_owner.TryEdit("Clear leveling", p => ResourceLeveler.ClearLeveling(p)))
            DialogResult = true;
    }
}
