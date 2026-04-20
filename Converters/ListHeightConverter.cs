using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace Rss_feeder_prout.Converters
{
    /// <summary>
    /// Calcule la hauteur nécessaire pour une CollectionView en fonction du nombre d'éléments.
    /// Ceci est nécessaire quand la CollectionView est dans un ScrollView.
    /// </summary>
    public class ListHeightConverter : IValueConverter
    {
        // La méthode de conversion est appelée : HeightRequest="{Binding CurrentPlaylist.UrlCount, Converter={StaticResource ListHeightConverter}, ConverterParameter=60}"
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Vérifie que la valeur est un nombre entier (le Count) et que le paramètre est la hauteur d'une ligne (ex: "60")
            if (value is int itemCount && parameter is string itemHeightString)
            {
                if (double.TryParse(itemHeightString, out double itemHeight))
                {
                    // Hauteur totale = Nombre d'éléments * Hauteur d'un élément.
                    double requiredHeight = itemCount * itemHeight;

                    // On définit une hauteur maximale pour éviter que la liste ne s'étende à l'infini. 
                    // 360 = 6 éléments * 60 (taille par défaut recommandée).
                    return Math.Min(requiredHeight, 360);
                }
            }

            // Retourne une valeur par défaut (hauteur d'un seul élément)
            return 60.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}