using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Planora.App.Dialogs;

/// <summary>Help > About Planora: version, credit and the open-source components Planora uses.</summary>
public sealed class AboutDialog : Window
{
    public AboutDialog()
    {
        Title = "About Planora";
        Style = (Style)Application.Current.Resources["DialogWindow"];
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0);

        var logo = new Grid { Width = 48, Height = 48, VerticalAlignment = VerticalAlignment.Top };
        logo.Children.Add(new Border { Background = Res("Navy"), CornerRadius = new CornerRadius(12) });
        logo.Children.Add(Bar(Brushes.White, 17.5, 9, 12.5));
        logo.Children.Add(Bar(new SolidColorBrush(Color.FromRgb(0x8F, 0x9A, 0xEA)), 23, 16, 21));
        logo.Children.Add(Bar(new SolidColorBrush(Color.FromArgb(0xBF, 0xFF, 0xFF, 0xFF)), 15, 24, 29.5));

        var text = new StackPanel { Margin = new Thickness(18, 0, 0, 0), Width = 360 };
        text.Children.Add(new TextBlock { Text = "Planora", FontSize = 22, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = "Project scheduling and control", Foreground = Res("TextSecondary"), Margin = new Thickness(0, 2, 0, 0) });
        text.Children.Add(new TextBlock { Text = $"Version {version.Major}.{version.Minor}.{version.Build}", Foreground = Res("TextMuted"), FontSize = 12, Margin = new Thickness(0, 10, 0, 0) });
        text.Children.Add(new Border
        {
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(12, 9, 12, 9),
            CornerRadius = new CornerRadius(8),
            Background = Res("AccentSoft"),
            Child = new TextBlock { Text = "Developed by Adeeb", FontWeight = FontWeights.SemiBold, Foreground = Res("Navy") },
        });
        text.Children.Add(new TextBlock
        {
            Text = "© 2026 Adeeb. Planora is free software.",
            Foreground = Res("TextMuted"),
            FontSize = 12,
            Margin = new Thickness(0, 12, 0, 0),
        });
        text.Children.Add(new TextBlock
        {
            Text = "Open-source components: MPXJ and MPXJ.Net (LGPL 2.1) for Microsoft Project files, IKVM (MIT), " +
                   "and the Inter typeface (SIL Open Font License 1.1). See THIRD-PARTY-NOTICES.md.",
            Foreground = Res("TextMuted"),
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        });

        var ok = new Button { Content = "Close", IsDefault = true, IsCancel = true, MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        ok.Click += (_, _) => Close();
        text.Children.Add(ok);

        Content = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(26, 24, 26, 22), Children = { logo, text } };
    }

    private static Border Bar(Brush brush, double width, double left, double top) => new()
    {
        Background = brush,
        Width = width,
        Height = 6,
        CornerRadius = new CornerRadius(3),
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(left, top, 0, 0),
    };

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];
}
