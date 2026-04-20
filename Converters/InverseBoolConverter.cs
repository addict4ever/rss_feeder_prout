using System;
using System.Globalization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Rss_feeder_prout.Converters // 🎯 Assurez-vous que ce namespace est correct
{
    /// <summary>
    /// Convertisseur qui inverse une valeur booléenne.
    /// Utilisé pour afficher un élément lorsque la condition inverse est vraie.
    /// </summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolean)
            {
                return !boolean;
            }
            // Retourne true pour la sécurité si la valeur est nulle ou non booléenne, 
            // pour ne pas masquer un élément par défaut.
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolean)
            {
                return !boolean;
            }
            return false;
        }
    }
}