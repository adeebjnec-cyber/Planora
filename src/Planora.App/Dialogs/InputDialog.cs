using System.Windows;
using System.Windows.Controls;

namespace Planora.App.Dialogs;

/// <summary>A one-line text prompt.</summary>
public static class InputDialog
{
    public static string? Ask(string title, string prompt, string initial = "")
    {
        var box = new TextBox { Text = initial, MinWidth = 320, Margin = new Thickness(0, 6, 0, 10), Padding = new Thickness(3, 2, 3, 2) };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } };
        var window = new Window
        {
            Title = title,
            Owner = Application.Current.MainWindow,
            Style = (Style)Application.Current.Resources["DialogWindow"],
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(14), Children = { new TextBlock { Text = prompt }, box, buttons } },
        };
        ok.Click += (_, _) => window.DialogResult = true;
        window.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        return window.ShowDialog() == true ? box.Text : null;
    }
}
