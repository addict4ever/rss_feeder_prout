using SQLite;
using Rss_feeder_prout.ViewModels;
using System;
using Microsoft.Maui.Controls; // Nécessaire pour ImageSource

namespace Rss_feeder_prout.Models
{
    public class FeedSite : BaseViewModel
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public string Name { get; set; }

        public string FeedUrl { get; set; }

        // L'URL distante de l'icône (récupérée via le flux RSS ou Favicon)
        public string IconUrl { get; set; }

        [Indexed]
        public int PlaylistId { get; set; }

        // --- Propriétés pour le mode hors-ligne ---

        private string _localIconPath;
        public string LocalIconPath
        {
            get => _localIconPath;
            set
            {
                if (SetProperty(ref _localIconPath, value))
                {
                    // On avertit l'UI que DisplayIcon a aussi changé
                    OnPropertyChanged(nameof(DisplayIcon));
                }
            }
        }

        [Ignore]
        public ImageSource DisplayIcon
        {
            get
            {
                if (string.IsNullOrEmpty(LocalIconPath))
                {
                    // Si on a une URL, on l'affiche, sinon une icône par défaut
                    return !string.IsNullOrEmpty(IconUrl)
                        ? ImageSource.FromUri(new Uri(IconUrl))
                        : ImageSource.FromFile("default_icon.png");
                }

                // Utilise le fichier local téléchargé
                return ImageSource.FromFile(LocalIconPath);
            }
        }

        // --- Propriétés d'État pour l'UI ---

        private bool _isBusy = false;
        [Ignore]
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        [Ignore]
        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? FeedUrl : Name;
    }
}