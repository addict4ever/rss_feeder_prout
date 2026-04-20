// Converters/IsNotNullConverter.cs
using System.Globalization;
using Microsoft.Maui.Controls;

namespace Rss_feeder_prout.Converters
{
    public class IsNotNullConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isNotNull = value != null;

            // Si un paramètre est fourni (et qu'il est "False" ou équivalent), inverse le résultat
            if (parameter is string paramString &&
                bool.TryParse(paramString, out bool invert) &&
                !invert)
            {
                // Si ConverterParameter=False, on retourne isNotNull
                return isNotNull;
            }
            else if (parameter is string paramStringInverse &&
                     bool.TryParse(paramStringInverse, out bool invertInverse) &&
                     invertInverse)
            {
                // Si ConverterParameter=True, on retourne l'inverse (est null)
                return !isNotNull;
            }

            // Par défaut, si aucun paramètre ou si c'est la valeur booléenne 'False' que l'on veut inverser
            // Ex: IsVisible="{Binding SelectedPlaylist, Converter={StaticResource IsNotNullConverter}}" -> IsNotNull (True si non null)
            return isNotNull;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}