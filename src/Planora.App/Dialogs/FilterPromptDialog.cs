using System.Windows;
using System.Windows.Controls;
using Planora.Engine.Views;

namespace Planora.App.Dialogs;

/// <summary>Asks for the values of an interactive filter (e.g. "Show tasks using resource:").</summary>
public sealed class FilterPromptDialog : Window
{
    private readonly List<(string Prompt, TextBox Box)> _boxes = new();

    private FilterPromptDialog(FilterDefinition filter)
    {
        Form.Setup(this, filter.Name.TrimEnd('.'));
        var panel = new StackPanel { Margin = new Thickness(14), Width = 340 };
        foreach (var prompt in filter.Prompts)
        {
            var box = Rows.Box(320);
            panel.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 6, 0, 2) });
            panel.Children.Add(box);
            _boxes.Add((prompt, box));
        }
        panel.Children.Add(Form.Buttons(this, () => DialogResult = true));
        Content = panel;
        Loaded += (_, _) => _boxes.FirstOrDefault().Box?.Focus();
    }

    /// <summary>The answers by prompt, or null if cancelled.</summary>
    public static IReadOnlyDictionary<string, string>? Ask(FilterDefinition filter)
    {
        var dialog = new FilterPromptDialog(filter);
        if (dialog.ShowDialog() != true)
            return null;
        return dialog._boxes.ToDictionary(b => b.Prompt, b => b.Box.Text.Trim());
    }
}
