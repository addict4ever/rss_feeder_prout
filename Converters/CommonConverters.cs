using System.Globalization;

namespace Rss_feeder_prout.Converters
{
    /// <summary>true si le nombre est &gt; 0 (ConverterParameter="invert" pour l'inverse).</summary>
    public class IntToBoolConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool result = value switch
            {
                int i => i > 0,
                long l => l > 0,
                double d => d > 0,
                _ => false
            };
            return parameter as string == "invert" ? !result : result;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    /// <summary>true si la chaîne n'est pas vide.</summary>
    public class StringNotEmptyConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool result = !string.IsNullOrWhiteSpace(value as string);
            return parameter as string == "invert" ? !result : result;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
