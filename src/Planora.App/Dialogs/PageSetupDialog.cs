using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Planora.App.ViewModels;
using Planora.Engine.Views;

namespace Planora.App.Dialogs;

/// <summary>Page Setup: paper, orientation, margins, header and footer, legend and printed columns.</summary>
public sealed class PageSetupDialog : Window
{
    private readonly MainViewModel _owner;
    private readonly ComboBox _paper = new() { Width = 120, ItemsSource = Enum.GetNames<PaperSize>() };
    private readonly RadioButton _landscape = new() { Content = "Landscape", GroupName = "orientation", Margin = new Thickness(0, 0, 14, 0) };
    private readonly RadioButton _portrait = new() { Content = "Portrait", GroupName = "orientation" };
    private readonly TextBox _margin = Rows.Box(60);
    private readonly TextBox _headerLeft = Rows.Box(170), _headerCenter = Rows.Box(170), _headerRight = Rows.Box(170);
    private readonly TextBox _footerLeft = Rows.Box(170), _footerCenter = Rows.Box(170), _footerRight = Rows.Box(170);
    private readonly CheckBox _legend = new() { Content = "Print a legend of the bar styles on every page", Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBox _columns = Rows.Box(50);

    public PageSetupDialog(MainViewModel owner)
    {
        _owner = owner;
        Form.Setup(this, "Page Setup");
        var s = owner.Project.PageSetup;
        _paper.SelectedIndex = (int)s.Paper;
        (s.Landscape ? _landscape : _portrait).IsChecked = true;
        _margin.Text = s.MarginMillimetres.ToString("0.#", CultureInfo.CurrentCulture);
        (_headerLeft.Text, _headerCenter.Text, _headerRight.Text) = (s.HeaderLeft, s.HeaderCenter, s.HeaderRight);
        (_footerLeft.Text, _footerCenter.Text, _footerRight.Text) = (s.FooterLeft, s.FooterCenter, s.FooterRight);
        _legend.IsChecked = s.Legend == LegendPlacement.EveryPage;
        _columns.Text = s.SheetColumns == 0 ? "" : s.SheetColumns.ToString(CultureInfo.CurrentCulture);

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Width = 700,
            Children =
            {
                Rows.Heading("Page"),
                Rows.Row(Rows.Label("Paper:", 90), _paper, Rows.Label("    Orientation:"), _landscape, _portrait),
                Rows.Row(Rows.Label("Margins:", 90), _margin, Rows.Label("mm")),
                Rows.Heading("Header"),
                Rows.Row(Rows.Label("Left / centre / right:", 130), _headerLeft, _headerCenter, _headerRight),
                Rows.Heading("Footer"),
                Rows.Row(Rows.Label("Left / centre / right:", 130), _footerLeft, _footerCenter, _footerRight),
                new TextBlock
                {
                    Text = "Codes: &[Page], &[Pages], &[Date], &[Time], &[Project Title], &[Project Start], &[Project Finish], &[Status Date], &[View]. The left header is printed as the title.",
                    TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.DimGray, Margin = new Thickness(0, 4, 0, 0),
                },
                Rows.Heading("View"),
                _legend,
                Rows.Row(Rows.Label("Print the first", 90), _columns, Rows.Label("columns of the table (empty = as many as fit in half the page)")),
                Form.Buttons(this, Apply),
            },
        };
    }

    private void Apply()
    {
        if (!double.TryParse(_margin.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var margin) || margin is < 0 or > 60)
        {
            Form.Warn(this, "Margins must be from 0 to 60 mm.");
            return;
        }
        int columns = 0;
        if (_columns.Text.Trim().Length > 0 && (!int.TryParse(_columns.Text, out columns) || columns < 1))
        {
            Form.Warn(this, "Enter a number of columns of 1 or more, or leave it empty.");
            return;
        }
        var setup = new PageSetup
        {
            Paper = (PaperSize)Math.Max(0, _paper.SelectedIndex),
            Landscape = _landscape.IsChecked == true,
            MarginMillimetres = margin,
            HeaderLeft = _headerLeft.Text, HeaderCenter = _headerCenter.Text, HeaderRight = _headerRight.Text,
            FooterLeft = _footerLeft.Text, FooterCenter = _footerCenter.Text, FooterRight = _footerRight.Text,
            Legend = _legend.IsChecked == true ? LegendPlacement.EveryPage : LegendPlacement.None,
            SheetColumns = columns,
        };
        if (_owner.TryEdit("Page setup", p => p.PageSetup = setup))
            DialogResult = true;
    }
}
