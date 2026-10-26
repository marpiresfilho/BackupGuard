using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BackupGuard.App;

/// <summary>
/// Liga um grupo de botões-pílula a um valor: a pílula fica marcada quando o valor é igual
/// ao parâmetro, e clicar nela grava o parâmetro. "null" representa "automático"/"nunca".
/// </summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value?.ToString() ?? "null", parameter?.ToString());

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true)
            return Binding.DoNothing;
        var text = parameter?.ToString();
        if (text is null or "null")
            return null;
        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return type == typeof(object) ? text : System.Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
    }
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

public sealed class InverseBoolToVisConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NullToVisConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
