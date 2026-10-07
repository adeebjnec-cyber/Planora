using System.Globalization;
using System.Windows.Data;

namespace Planora.App.Infrastructure;

/// <summary>True when all bound values are equal (used to check the current item in name menus).</summary>
public sealed class EqualsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length > 1 && values.Skip(1).All(v => Equals(v, values[0]));

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
