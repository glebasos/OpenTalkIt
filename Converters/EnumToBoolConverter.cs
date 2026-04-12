using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace OpenTalkIt.Converters;

public class EnumToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.Equals(parameter) ?? false;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? parameter : Avalonia.Data.BindingOperations.DoNothing;

}