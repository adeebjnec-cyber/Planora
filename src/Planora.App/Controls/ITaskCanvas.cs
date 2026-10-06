using Planora.App.ViewModels;
using Planora.Engine.Model;

namespace Planora.App.Controls;

/// <summary>A custom-drawn task view (network diagram, calendar, board, timeline) hosted in a <see cref="Views.DiagramView"/>.</summary>
public interface ITaskCanvas
{
    MainViewModel? ViewModel { get; set; }
    event EventHandler<ProjectTask>? TaskClicked;
    event EventHandler<ProjectTask>? TaskDoubleClicked;
}
