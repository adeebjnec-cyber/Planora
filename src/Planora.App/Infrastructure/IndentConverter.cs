using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Planora.App.Infrastructure;

/// <summary>Turns an indent width into a left margin, for outline indentation in the task sheet.</summary>
public sealed class IndentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        new Thickness(value is double d ? d : 0, 0, 0, 0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
