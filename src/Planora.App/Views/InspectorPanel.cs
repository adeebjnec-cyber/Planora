using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Planora.App.ViewModels;
using Planora.Engine.Scheduling;

namespace Planora.App.Views;

/// <summary>Task Inspector pane (Task > Inspect): what drives the selected task's dates, and its problems.</summary>
public sealed class InspectorPanel : UserControl
{
    private readonly StackPanel _items = new() { Margin = new Thickness(12) };
    private MainViewModel? _viewModel;

    public InspectorPanel()
    {
        Background = Brushes.White;
        BorderBrush = Brushes.LightGray;
        BorderThickness = new Thickness(1, 0, 0, 0);
        var close = new Button { Content = "✕", Padding = new Thickness(6, 0, 6, 0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) =>
        {
            if (_viewModel is not null)
                _viewModel.ShowInspector = false;
        };
        var title = new DockPanel { Margin = new Thickness(12, 8, 6, 0) };
        DockPanel.SetDock(close, Dock.Right);
        title.Children.Add(close);
        title.Children.Add(new TextBlock { Text = "Task Inspector", FontSize = 16, FontWeight = FontWeights.SemiBold });
        var root = new DockPanel();
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _items });
        Content = root;

        DataContextChanged += (_, _) =>
        {
            if (_viewModel is not null)
                _viewModel.Refreshed -= OnRefreshed;
            _viewModel = DataContext as MainViewModel;
            if (_viewModel is not null)
                _viewModel.Refreshed += OnRefreshed;
            Load();
        };
        IsVisibleChanged += (_, _) => Load();
    }

    private void OnRefreshed(object? sender, EventArgs e)
    {
        if (IsVisible)
            Load();
    }

    private void Load()
    {
        _items.Children.Clear();
        if (_viewModel?.SelectedTasks.FirstOrDefault() is not { } selected || _viewModel.Project.GetTask(selected.Id) is not { } task)
        {
            _items.Children.Add(new TextBlock { Text = "Select a task to see what drives its dates.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray });
            return;
        }
        var project = _viewModel.Project;
        _items.Children.Add(new TextBlock { Text = $"{project.IndexOf(task) + 1}. {task.Name}", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        var items = TaskInspector.Inspect(project, task);
        if (items.Any(i => i.IsWarning))
            _items.Children.Add(new TextBlock { Text = "Problems", FontWeight = FontWeights.SemiBold, Foreground = Brushes.Firebrick, Margin = new Thickness(0, 4, 0, 4) });
        foreach (var item in items.Where(i => i.IsWarning))
            _items.Children.Add(Card(item, warning: true));
        _items.Children.Add(new TextBlock { Text = "Factors affecting the task", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
        foreach (var item in items.Where(i => !i.IsWarning))
            _items.Children.Add(Card(item, warning: false));
    }

    private static Border Card(InspectorItem item, bool warning) => new()
    {
        Background = new SolidColorBrush(warning ? Color.FromRgb(0xFD, 0xEC, 0xEA) : Color.FromRgb(0xF3, 0xF6, 0xFA)),
        BorderBrush = new SolidColorBrush(warning ? Color.FromRgb(0xE8, 0xB4, 0xAE) : Color.FromRgb(0xD5, 0xDE, 0xEA)),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(8, 6, 8, 6),
        Margin = new Thickness(0, 0, 0, 6),
        Child = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = item.Heading, FontWeight = FontWeights.SemiBold },
                new TextBlock { Text = item.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) },
            },
        },
    };
}
