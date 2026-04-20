using SQLite;
using Rss_feeder_prout.ViewModels;
using System;
using Rss_feeder_prout.Helpers; // DOIT contenir la classe HtmlHelper avec la méthode StripHtml()
using System.ComponentModel;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Controls; // Nécessaire si BaseViewModel hérite de ObservableObject ou utilise SetProperty

namespace Rss_feeder_prout.Models
{
    // 🎯 ASSUREZ-VOUS QUE BaseViewModel hérite de INotifyPropertyChanged
    public class RssItem : BaseViewModel
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public string Title { get; set; }
        public string Summary { get; set; }
        public string PublishDate { get; set; }
        public string ArticleGuid { get; set; }
        public string Link { get; set; }

        [Indexed]
        public int PlaylistId { get; set; }

        // ✅ CORRECT : Clé étrangère pour le filtrage par site
        [Indexed]
        public int? SiteId { get; set; }

        // --- PROPRIÉTÉ CORRIGÉE : SiteName (Ignorée par SQLite) ---
        private string _siteName;
        [Ignore] // Ne pas stocker dans la DB, récupéré du Feed lors de la synchro
        public string SiteName
        {
            get => _siteName;
            set => SetProperty(ref _siteName, value);
        }
        // ---------------------------------------------------------

        private bool _isRead = false;
        public bool IsRead
        {
            get => _isRead;
            set
            {
                if (SetProperty(ref _isRead, value))
                {
                    OnPropertyChanged(nameof(BackgroundColor));
                }
            }
        }

        // ✅ CORRECT : Champs pour le contenu enrichi
        public string Author { get; set; }

        public string ImageUrl { get; set; }

        // Stocke le contenu HTML complet (pour lecture hors ligne)
        [MaxLength(25000)] // Limite la taille pour ne pas surcharger la DB
        public string ContentHtml { get; set; }

        // --- PROPRIÉTÉS LIÉES AU TÉLÉCHARGEMENT ---
        private bool _isDownloaded = false;
        [Column("IsDownloaded")]
        public bool IsDownloaded
        {
            get => _isDownloaded;
            set
            {
                if (SetProperty(ref _isDownloaded, value))
                {
                    OnPropertyChanged(nameof(IsNotDownloaded));
                    OnPropertyChanged(nameof(PreviewText));
                }
            }
        }

        [Ignore]
        public bool IsNotDownloaded => !IsDownloaded;

        private bool _isBusy = false;
        [Ignore]
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        // ✅ CORRECT : Propriété de couleur dynamique
        [Ignore]
        public Color BackgroundColor
        {
            // Changement de couleur au lieu de Gris clair
            get => IsRead ? Color.FromArgb("#ADD8E6") : Colors.White; // Bleu ciel pour lu, Blanc pour non lu
        }

        // ✅ CORRECT : Utilise ContentHtml s'il est téléchargé, sinon Summary
        [Ignore]
        public string PreviewText
        {
            get
            {
                // 🎯 L'élément clé : Utiliser le contenu HTML téléchargé si disponible
                string sourceText = !string.IsNullOrWhiteSpace(ContentHtml)
            ? HtmlHelper.StripHtml(ContentHtml) // Supprimer le HTML pour l'aperçu
                        : Summary;

                if (string.IsNullOrWhiteSpace(sourceText))
                {
                    return "Aucun aperçu disponible.";
                }

                const int maxLength = 1200;

                if (sourceText.Length > maxLength)
                {
                    return sourceText.Substring(0, maxLength).Trim() + "...";
                }

                return sourceText.Trim();
            }
        }

        // --- Autres Propriétés Ignorées ---

        [Ignore]
        public Uri LinkUri => Link != null ? new Uri(Link) : null;
    }
}