// Rss_feeder_prout/Converters/NullToBoolConverter.cs
using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace Rss_feeder_prout.Converters
{
    // Convertit un objet null ou non-null en un booléen (True si non-null, False si null)
    public class NullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Retourne true si la valeur n'est PAS nulle
            return value != null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // La conversion inverse n'est pas nécessaire pour ce cas d'usage
            return value;
        }
    }
}