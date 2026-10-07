using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Planora.App.ViewModels;

namespace Planora.App.Dialogs;

/// <summary>
/// Ctrl+K: search every command in the menus, the views, the tables, filters and groups, and the tasks,
/// by name; Enter runs the highlighted one.
/// </summary>
public sealed class CommandPalette : Window
{
    private sealed record Entry(string Label, string Category, string Detail, Action Run, bool Enabled = true);

    private readonly MainWindow _owner;
    private readonly List<Entry> _entries = new();
    private readonly TextBox _input;
    private readonly TextBlock _placeholder;
    private readonly ListBox _list;
    private bool _closing;

    public CommandPalette(MainWindow owner, Menu menu)
    {
        _owner = owner;
        Owner = owner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Width = 640;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        FontFamily = (FontFamily)Application.Current.Resources["UiFontFamily"];
        FontSize = 13;

        Collect(menu.Items, "");
        var vm = owner.ViewModel;
        foreach (var view in vm.Views)
            _entries.Add(new Entry(view.Title, "View", "", () => vm.CurrentView = view.Kind));
        foreach (var table in vm.TableNames)
            _entries.Add(new Entry("Table: " + table, "View", "", () => vm.CurrentTableName = table));
        foreach (var filter in vm.FilterNames)
            _entries.Add(new Entry("Filter: " + filter, "View", "", () => vm.CurrentFilterName = filter));
        foreach (var group in vm.GroupNames)
            _entries.Add(new Entry("Group: " + group, "View", "", () => vm.CurrentGroupName = group));
        foreach (var task in vm.Project.Tasks)
        {
            var t = task;
            _entries.Add(new Entry(t.Name, t.IsSummary ? "Summary" : "Task", Infrastructure.DateText.Format(t.Start), () =>
            {
                vm.SelectTask(t);
                if (vm.ScrollToTaskCommand.CanExecute(null))
                    vm.ScrollToTaskCommand.Execute(null);
            }));
        }

        _input = new TextBox
        {
            BorderThickness = new Thickness(0),
            FontSize = 14.5,
            Padding = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
        };
        _placeholder = new TextBlock { Text = "Search commands, views and tasks…", FontSize = 14.5, Foreground = Brush("#8A93A2"), IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
        _input.TextChanged += (_, _) => Refresh();
        _input.PreviewKeyDown += OnKey;

        _list = new ListBox
        {
            BorderThickness = new Thickness(0),
            MaxHeight = 420,
            Padding = new Thickness(5),
            FocusVisualStyle = null,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.ItemContainerStyle = ItemStyle();
        _list.MouseUp += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left && _list.SelectedItem is Entry entry)
                RunEntry(entry);
        };

        var search = new DockPanel { Height = 50, Margin = new Thickness(16, 0, 14, 0) };
        var esc = new Border
        {
            BorderBrush = Brush("#E4E6EB"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 1, 5, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "Esc", FontSize = 10.5, Foreground = Brush("#8A93A2") },
        };
        DockPanel.SetDock(esc, Dock.Right);
        search.Children.Add(esc);
        var icon = new Infrastructure.LineIcon { Kind = "find", Size = 17, Foreground = Brush("#8A93A2"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        DockPanel.SetDock(icon, Dock.Left);
        search.Children.Add(icon);
        search.Children.Add(new Grid { Children = { _placeholder, _input } });

        var panel = new DockPanel();
        var top = new Border { BorderBrush = Brush("#EEF0F3"), BorderThickness = new Thickness(0, 0, 0, 1), Child = search };
        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);
        var footer = new TextBlock
        {
            Text = "↑↓ to move   ·   Enter to run   ·   Planora, developed by Adeeb",
            FontSize = 11,
            Foreground = Brush("#B3BAC6"),
            Margin = new Thickness(16, 7, 16, 8),
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        panel.Children.Add(footer);
        panel.Children.Add(_list);

        Content = new Border
        {
            Margin = new Thickness(24, 12, 24, 36),
            Background = Brushes.White,
            BorderBrush = Brush("#E4E6EB"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 12, Direction = 270, Opacity = 0.22, Color = Color.FromRgb(0x14, 0x1A, 0x26) },
            Child = panel,
        };

        Loaded += (_, _) =>
        {
            var origin = owner.PointToScreen(new Point(0, 0));
            var dpi = VisualTreeHelper.GetDpi(owner);
            Left = origin.X / dpi.DpiScaleX + (owner.ActualWidth - Width) / 2;
            Top = origin.Y / dpi.DpiScaleY + 70;
            _input.Focus();
        };
        Deactivated += (_, _) => SafeClose();
        Refresh();
    }

    /// <summary>Stays open when it loses focus (screen captures).</summary>
    public bool KeepOpen { get; set; }

    /// <summary>Starts with this search text.</summary>
    public string SearchText
    {
        get => _input.Text;
        set => _input.Text = value;
    }

    private void SafeClose()
    {
        if (_closing || KeepOpen && !_running)
            return;
        _closing = true;
        Close();
    }

    /// <summary>Adds every runnable menu item, with its menu path as the category.</summary>
    private void Collect(ItemCollection items, string path)
    {
        foreach (var item in items.OfType<MenuItem>())
        {
            var header = item.Header as string ?? "";
            var label = header.Replace("...", "").Replace("_", "");
            if (item.Items.Count > 0 && item.ItemsSource is null)
            {
                Collect(item.Items, path.Length == 0 ? label : path + " › " + label);
                continue;
            }
            if (item.ItemsSource is not null || label.Length == 0)
                continue;
            var menuItem = item;
            if (menuItem.Command is { } command)
            {
                var parameter = menuItem.CommandParameter;
                _entries.Add(new Entry(label, path, menuItem.InputGestureText, () =>
                {
                    if (command.CanExecute(parameter))
                        command.Execute(parameter);
                }));
            }
            else if (menuItem.IsCheckable)
            {
                _entries.Add(new Entry((menuItem.IsChecked ? "Hide " : "Show ") + label, path, "", () => menuItem.IsChecked = !menuItem.IsChecked));
            }
            else
            {
                _entries.Add(new Entry(label, path, menuItem.InputGestureText, () => menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, menuItem))));
            }
        }
    }

    private void Refresh()
    {
        _placeholder.Visibility = _input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var words = _input.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matches = _entries
            .Where(e => words.All(w => e.Label.Contains(w, StringComparison.CurrentCultureIgnoreCase) || e.Category.Contains(w, StringComparison.CurrentCultureIgnoreCase)))
            .OrderBy(e => words.Length > 0 && e.Label.StartsWith(words[0], StringComparison.CurrentCultureIgnoreCase) ? 0 : 1)
            .ThenBy(e => e.Category is "Task" or "Summary" ? 1 : 0)
            .Take(60)
            .ToList();
        _list.ItemsSource = matches;
        _list.SelectedIndex = matches.Count > 0 ? 0 : -1;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                _list.SelectedIndex = Math.Min(_list.Items.Count - 1, _list.SelectedIndex + 1);
                _list.ScrollIntoView(_list.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up:
                _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1);
                _list.ScrollIntoView(_list.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                if (_list.SelectedItem is Entry entry)
                    RunEntry(entry);
                e.Handled = true;
                break;
            case Key.Escape:
                KeepOpen = false;
                SafeClose();
                e.Handled = true;
                break;
        }
    }

    private bool _running;

    private void RunEntry(Entry entry)
    {
        _running = true;
        SafeClose();
        _owner.Dispatcher.BeginInvoke(entry.Run);
    }

    private static Style ItemStyle()
    {
        var factory = new FrameworkElementFactory(typeof(Border), "Bd");
        factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        factory.SetValue(Border.PaddingProperty, new Thickness(11, 7, 11, 7));
        factory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        factory.AppendChild(content);
        var template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = factory };
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, Brush("#EEF0FC"), "Bd"));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Brush("#F4F5F8"), "Bd"));
        template.Triggers.Add(hover);
        template.Triggers.Add(selected);

        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        style.Setters.Add(new Setter(ContentControl.ContentTemplateProperty, EntryTemplate()));
        return style;
    }

    private static DataTemplate EntryTemplate()
    {
        var dock = new FrameworkElementFactory(typeof(DockPanel));
        var category = new FrameworkElementFactory(typeof(TextBlock));
        category.SetValue(DockPanel.DockProperty, Dock.Right);
        category.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Entry.Category)));
        category.SetValue(TextBlock.ForegroundProperty, Brush("#8A93A2"));
        category.SetValue(TextBlock.FontSizeProperty, 11.5);
        category.SetValue(FrameworkElement.MarginProperty, new Thickness(14, 0, 0, 0));
        category.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        var detail = new FrameworkElementFactory(typeof(TextBlock));
        detail.SetValue(DockPanel.DockProperty, Dock.Right);
        detail.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Entry.Detail)));
        detail.SetValue(TextBlock.ForegroundProperty, Brush("#B3BAC6"));
        detail.SetValue(TextBlock.FontSizeProperty, 11.5);
        detail.SetValue(FrameworkElement.MarginProperty, new Thickness(14, 0, 0, 0));
        detail.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Entry.Label)));
        label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        label.SetValue(TextBlock.ForegroundProperty, Brush("#141A26"));
        dock.AppendChild(category);
        dock.AppendChild(detail);
        dock.AppendChild(label);
        return new DataTemplate { VisualTree = dock };
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
