using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Planora.App.Infrastructure;
using Planora.App.ViewModels;
using Planora.Engine.Model;

namespace Planora.App.Views;

/// <summary>The Resource Sheet: one editable row per resource, over-allocated resources in red.</summary>
public partial class ResourceSheetView : UserControl
{
    public ResourceSheetView()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel?.SetResourceSelection(Sheet.SelectedItems.OfType<ResourceRowViewModel>().Select(r => r.Resource).OfType<Resource>());

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is null || Keyboard.FocusedElement is TextBox or ComboBox)
            return;
        if (e.Key == Key.Delete && ViewModel.DeleteResourceCommand.CanExecute(null))
        {
            ViewModel.DeleteResourceCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F2 && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            ViewModel.ResourceInformationCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.Item is ResourceRowViewModel { IsPlaceholder: true } && e.Column.Header as string != "Resource Name")
            e.Cancel = true;
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement element &&
            VisualTreeSearch.FindParent<DataGridCell>(element) is { Column.DisplayIndex: 0 } &&
            element.DataContext is ResourceRowViewModel { IsPlaceholder: false })
        {
            ViewModel?.ResourceInformationCommand.Execute(null);
            e.Handled = true;
        }
    }
}
