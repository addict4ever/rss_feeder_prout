// Converters/PlaylistToColorConverter.cs
using System.Globalization;
using Rss_feeder_prout.Models;
using Microsoft.Maui.Controls;

namespace Rss_feeder_prout.Converters
{
    public class PlaylistToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // value est la CurrentPlaylist (objet sélectionné)
            // parameter est la Playlist en cours de rendu (objet à comparer)

            if (value is FeedPlaylist current && parameter is FeedPlaylist item)
            {
                // Comparaison par ID pour vérifier si c'est la playlist sélectionnée
                if (current.Id == item.Id)
                {
                    // Couleur de mise en évidence (par exemple, un bleu foncé)
                    return Color.FromArgb("#FF6200EE"); // Utilisez une couleur de votre thème
                }
            }
            // Couleur par défaut (par exemple, gris clair)
            return Color.FromArgb("#F0F0F0");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Conversion inverse non nécessaire ici
            return null;
        }
    }
}