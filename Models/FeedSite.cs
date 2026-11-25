using SQLite;
using Rss_feeder_prout.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Rss_feeder_prout.Models
{
    /// <summary>
    /// Une source (flux RSS / Atom / RDF / JSON Feed). Stockée dans Databases/RssConfig.db3.
    /// NB : les nouveaux booléens utilisent une logique "négative" (IsDisabled...) pour que
    /// les anciennes lignes (colonne ajoutée = 0) gardent le comportement par défaut.
    /// </summary>
    public class FeedSite : ObservableModel
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set
            {
                if (SetProperty(ref _name, value ?? string.Empty))
                    Notify(nameof(DisplayName), nameof(Initial));
            }
        }

        public string FeedUrl { get; set; } = string.Empty;

        /// <summary>Page d'accueil du site (déduite du flux).</summary>
        public string SiteUrl { get; set; } = string.Empty;

        /// <summary>L'URL distante de l'icône (récupérée via le flux RSS ou Favicon).</summary>
        public string IconUrl { get; set; } = string.Empty;

        /// <summary>Éléments CSS à RETIRER de l'article (ex: ".ads, .social-share, aside").</summary>
        public string CustomCleanSelectors { get; set; } = string.Empty;

        /// <summary>NOUVEAU : sélecteur CSS du contenu principal (ex: "article .entry-content").</summary>
        public string ContentSelector { get; set; } = string.Empty;

        [Indexed]
        public int PlaylistId { get; set; }

        private string _localIconPath = string.Empty;
        public string LocalIconPath
        {
            get => _localIconPath;
            set
            {
                if (SetProperty(ref _localIconPath, value ?? string.Empty))
                    Notify(nameof(DisplayIcon), nameof(HasIcon));
            }
        }

        private bool _isDisabled;
        /// <summary>Source mise en pause : ignorée par les synchronisations.</summary>
        public bool IsDisabled
        {
            get => _isDisabled;
            set
            {
                if (SetProperty(ref _isDisabled, value))
                    Notify(nameof(HealthText), nameof(HealthColor), nameof(IsEnabled));
            }
        }

        /// <summary>Toujours télécharger l'article complet pour ce site, même en synchro rapide.</summary>
        public bool AlwaysDownloadFull { get; set; }

        /// <summary>Ouvrir directement dans le navigateur (sites incompatibles avec le mode lecture).</summary>
        public bool OpenInBrowser { get; set; }

        // --- Requêtes conditionnelles (économie de données) ---
        public string ETag { get; set; } = string.Empty;
        public string LastModified { get; set; } = string.Empty;

        // --- Santé du flux ---
        public DateTime? LastFetchedUtc { get; set; }
        public DateTime? LastSuccessUtc { get; set; }

        private string _lastError = string.Empty;
        public string LastError
        {
            get => _lastError;
            set
            {
                if (SetProperty(ref _lastError, value ?? string.Empty))
                    Notify(nameof(HealthText), nameof(HealthColor), nameof(HasError));
            }
        }

        private int _errorCount;
        public int ErrorCount
        {
            get => _errorCount;
            set
            {
                if (SetProperty(ref _errorCount, value))
                    Notify(nameof(HealthText), nameof(HealthColor), nameof(HasError));
            }
        }

        public int SortOrder { get; set; }

        // ------------------------------------------------------------------
        // UI (non stocké)
        // ------------------------------------------------------------------

        private bool _isBusy;
        [Ignore]
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        private int _unreadCount;
        [Ignore]
        public int UnreadCount
        {
            get => _unreadCount;
            set
            {
                if (SetProperty(ref _unreadCount, value))
                    Notify(nameof(HasUnread), nameof(UnreadText));
            }
        }

        private int _totalCount;
        [Ignore]
        public int TotalCount
        {
            get => _totalCount;
            set
            {
                if (SetProperty(ref _totalCount, value))
                    OnPropertyChanged(nameof(CountText));
            }
        }

        [Ignore] public bool HasUnread => UnreadCount > 0;
        [Ignore] public string UnreadText => UnreadCount > 999 ? "999+" : UnreadCount.ToString();
        [Ignore] public string CountText => TotalCount == 1 ? "1 article" : $"{TotalCount} articles";
        [Ignore] public bool IsEnabled => !IsDisabled;
        [Ignore] public bool HasError => ErrorCount > 0 && !string.IsNullOrEmpty(LastError);

        [Ignore]
        public string HealthText
        {
            get
            {
                if (IsDisabled) return "En pause";
                if (HasError) return ErrorCount > 1 ? $"Erreur ({ErrorCount}×) : {LastError}" : $"Erreur : {LastError}";
                if (LastSuccessUtc.HasValue)
                    return "OK · " + Helpers.DateHelper.ToRelative(DateTime.SpecifyKind(LastSuccessUtc.Value, DateTimeKind.Utc).ToLocalTime());
                return "Jamais synchronisé";
            }
        }

        [Ignore]
        public Color HealthColor
        {
            get
            {
                if (IsDisabled) return ThemeService.Palette.TextMuted;
                if (HasError) return ErrorCount >= 3 ? ThemeService.Palette.Danger : ThemeService.Palette.Warning;
                return LastSuccessUtc.HasValue ? ThemeService.Palette.Success : ThemeService.Palette.TextMuted;
            }
        }

        [Ignore]
        public bool HasIcon => !string.IsNullOrEmpty(LocalIconPath) && File.Exists(LocalIconPath);

        [Ignore]
        public ImageSource? DisplayIcon
        {
            get
            {
                if (!string.IsNullOrEmpty(LocalIconPath) && File.Exists(LocalIconPath))
                    return ImageSource.FromFile(LocalIconPath);

                if (!string.IsNullOrEmpty(IconUrl) && Uri.TryCreate(IconUrl, UriKind.Absolute, out var uri))
                    return ImageSource.FromUri(uri);

                return null;
            }
        }

        [Ignore]
        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? FeedUrl : Name;

        [Ignore]
        public string Initial => string.IsNullOrWhiteSpace(DisplayName) ? "?" : DisplayName.Trim().Substring(0, 1).ToUpperInvariant();

        [Ignore]
        public string Host
        {
            get
            {
                var url = !string.IsNullOrWhiteSpace(SiteUrl) ? SiteUrl : FeedUrl;
                return Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host.Replace("www.", "") : url;
            }
        }

        public void RefreshHealth() => Notify(nameof(HealthText), nameof(HealthColor), nameof(HasError), nameof(IsEnabled));
    }
}
