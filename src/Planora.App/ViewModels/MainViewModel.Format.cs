using System.Windows.Input;
using Planora.App.Dialogs;
using Planora.App.Infrastructure;

namespace Planora.App.ViewModels;

/// <summary>Format tab: bar styles, text styles, timescale and gridlines.</summary>
public sealed partial class MainViewModel
{
    public ICommand BarStylesCommand { get; private set; } = null!;
    public ICommand TextStylesCommand { get; private set; } = null!;
    public ICommand TimescaleCommand { get; private set; } = null!;
    public ICommand GridlinesCommand { get; private set; } = null!;

    private void InitializeFormatCommands()
    {
        BarStylesCommand = new RelayCommand(() => ShowDialog(new BarStylesDialog(this)));
        TextStylesCommand = new RelayCommand(() => ShowDialog(new TextStylesDialog(this)));
        TimescaleCommand = new RelayCommand(() => ShowDialog(new TimescaleDialog(this)));
        GridlinesCommand = new RelayCommand(() => ShowDialog(new GridlinesDialog(this)));
    }
}
