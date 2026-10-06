using System.Windows;
using System.Windows.Controls;
using Planora.App.Controls;
using Planora.App.ViewModels;

namespace Planora.App.Views;

/// <summary>
/// Hosts a custom-drawn task view (network diagram, calendar, board, timeline) in a scroll viewer and
/// connects it to the view model: clicks select tasks, double-clicks open Task Information.
/// </summary>
public sealed class DiagramView : UserControl
{
    public DiagramView(FrameworkElement content, bool scroll = true)
    {
        Content = scroll
            ? new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Drawing.Background,
                Content = content,
            }
            : content;

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainViewModel vm)
                return;
            if (content is ITaskCanvas canvas && canvas.ViewModel is null)
            {
                canvas.ViewModel = vm;
                canvas.TaskClicked += (_, t) => vm.SetSelection(new[] { t });
                canvas.TaskDoubleClicked += (_, _) => vm.TaskInformationCommand.Execute(null);
            }
        };
    }
}
