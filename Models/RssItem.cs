using SQLite;
using Rss_feeder_prout.Helpers;
using Rss_feeder_prout.Services;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Controls;

namespace Rss_feeder_prout.Models
{
    /// <summary>
    /// État d'un article du point de vue du stockage.
    /// Online  = vu dans le flux en direct, PAS encore enregistré dans la base.
    /// Cached  = enregistré dans la base (titre + résumé), contenu complet pas encore téléchargé.
    /// Offline = contenu complet téléchargé, lisible sans connexion.
    /// </summary>
    public enum ArticleState
    {
        Online,
        Cached,
        Offline
    }

    /// <summary>
    /// Article RSS. Stocké dans Databases/RssArticles.db3 (une seule base, plus de fichiers par jour).
    /// L'unicité est garantie par le couple (SiteId, ArticleGuid).
    /// </summary>
    [Table("RssItem")]
    public class RssItem : ObservableModel
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed(Name = "UX_RssItem_Site_Guid", Order = 2, Unique = true)]
        public string ArticleGuid { get; set; } = string.Empty;

        private string _title = string.Empty;
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value ?? string.Empty);
        }

        private string _summary = string.Empty;
        public string Summary
        {
            get => _summary;
            set
            {
                if (SetProperty(ref _summary, value ?? string.Empty))
                    Notify(nameof(PreviewText), nameof(ShowSummary));
            }
        }

        public string Link { get; set; } = string.Empty;

        public string Author { get; set; } = string.Empty;

        private string _imageUrl = string.Empty;
        /// <summary>URL distante de l'image principale (ou ancien chemin local pour les vieilles bases).</summary>
        public string ImageUrl
        {
            get => _imageUrl;
            set
            {
                if (SetProperty(ref _imageUrl, value ?? string.Empty))
                    Notify(nameof(ThumbSource), nameof(HasThumb));
            }
        }

        private string _localImagePath = string.Empty;
        /// <summary>Chemin local de l'image principale (mode hors-ligne).</summary>
        public string LocalImagePath
        {
            get => _localImagePath;
            set
            {
                if (SetProperty(ref _localImagePath, value ?? string.Empty))
                    Notify(nameof(ThumbSource), nameof(HasThumb));
            }
        }

        /// <summary>Ancienne date texte (conservée pour compatibilité / affichage brut).</summary>
        public string PublishDate { get; set; } = string.Empty;

        /// <summary>Vraie date de publication (UTC) — sert au tri, au filtrage et au nettoyage.</summary>
        [Indexed]
        public DateTime PublishDateUtc { get; set; }

        /// <summary>Date à laquelle l'article est entré dans la base (UTC).</summary>
        public DateTime FetchedAtUtc { get; set; }

        [Indexed]
        public int PlaylistId { get; set; }

        [Indexed(Name = "UX_RssItem_Site_Guid", Order = 1, Unique = true)]
        public int? SiteId { get; set; }

        private string _siteName = string.Empty;
        /// <summary>Nom du site (maintenant STOCKÉ en base : avant il était perdu).</summary>
        public string SiteName
        {
            get => _siteName;
            set
            {
                if (SetProperty(ref _siteName, value ?? string.Empty))
                    OnPropertyChanged(nameof(SiteInitial));
            }
        }

        private bool _isRead;
        [Indexed]
        public bool IsRead
        {
            get => _isRead;
            set
            {
                if (SetProperty(ref _isRead, value))
                    Notify(nameof(BackgroundColor), nameof(TitleOpacity), nameof(TitleAttributes), nameof(ShowUnreadDot));
            }
        }

        public DateTime? ReadAtUtc { get; set; }

        private bool _isFavorite;
        public bool IsFavorite
        {
            get => _isFavorite;
            set => SetProperty(ref _isFavorite, value);
        }

        private bool _isReadLater;
        public bool IsReadLater
        {
            get => _isReadLater;
            set => SetProperty(ref _isReadLater, value);
        }

        private bool _isDownloaded;
        [Column("IsDownloaded")]
        public bool IsDownloaded
        {
            get => _isDownloaded;
            set
            {
                if (SetProperty(ref _isDownloaded, value))
                    NotifyState();
            }
        }

        public DateTime? DownloadedAtUtc { get; set; }

        private bool _imagesCached;
        /// <summary>Les images du contenu ont été copiées localement.</summary>
        public bool ImagesCached
        {
            get => _imagesCached;
            set
            {
                if (SetProperty(ref _imagesCached, value))
                    NotifyState();
            }
        }

        /// <summary>Contenu HTML complet (lecture hors-ligne). Plus de limite de taille arbitraire.</summary>
        public string ContentHtml { get; set; } = string.Empty;

        private int _readingMinutes;
        public int ReadingMinutes
        {
            get => _readingMinutes;
            set
            {
                if (SetProperty(ref _readingMinutes, value))
                    Notify(nameof(ReadingTimeText), nameof(HasReadingTime));
            }
        }

        /// <summary>Texte normalisé (minuscules, sans accents) pour la recherche plein texte.</summary>
        public string SearchBlob { get; set; } = string.Empty;

        public string Categories { get; set; } = string.Empty;

        // ------------------------------------------------------------------
        // Propriétés d'état UI (non stockées)
        // ------------------------------------------------------------------

        private bool _isLive;
        /// <summary>Article vu en direct dans le flux mais pas (encore) enregistré.</summary>
        [Ignore]
        public bool IsLive
        {
            get => _isLive;
            set
            {
                if (SetProperty(ref _isLive, value))
                    NotifyState();
            }
        }

        private bool _isBusy;
        [Ignore]
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        private bool _isHighlighted;
        /// <summary>Contient un mot-clé surveillé (Options > Filtres).</summary>
        [Ignore]
        public bool IsHighlighted
        {
            get => _isHighlighted;
            set => SetProperty(ref _isHighlighted, value);
        }

        private string _siteIconPath = string.Empty;
        [Ignore]
        public string SiteIconPath
        {
            get => _siteIconPath;
            set
            {
                if (SetProperty(ref _siteIconPath, value ?? string.Empty))
                    Notify(nameof(SiteIconSource), nameof(HasSiteIcon));
            }
        }

        // ------------------------------------------------------------------
        // Propriétés calculées pour l'affichage
        // ------------------------------------------------------------------

        [Ignore]
        public ArticleState State
        {
            get
            {
                if (IsLive && Id == 0) return ArticleState.Online;
                return IsDownloaded ? ArticleState.Offline : ArticleState.Cached;
            }
        }

        [Ignore]
        public string StatusText => State switch
        {
            ArticleState.Online => "En ligne",
            ArticleState.Offline => ImagesCached ? "Hors-ligne + images" : "Hors-ligne",
            _ => "Résumé"
        };

        [Ignore]
        public string StatusIcon => State switch
        {
            ArticleState.Online => "globe",
            ArticleState.Offline => "check-circle",
            _ => "doc"
        };

        [Ignore]
        public Color StatusColor => State switch
        {
            ArticleState.Online => ThemeService.Palette.Info,
            ArticleState.Offline => ThemeService.Palette.Success,
            _ => ThemeService.Palette.Warning
        };

        [Ignore]
        public Color StatusSoftColor => State switch
        {
            ArticleState.Online => ThemeService.Palette.InfoSoft,
            ArticleState.Offline => ThemeService.Palette.SuccessSoft,
            _ => ThemeService.Palette.WarningSoft
        };

        [Ignore]
        public bool IsNotDownloaded => !IsDownloaded;

        [Ignore]
        public bool IsStored => Id > 0;

        [Ignore]
        public DateTime PublishedLocal
        {
            get
            {
                if (PublishDateUtc == default) return DateTime.Now;
                return DateTime.SpecifyKind(PublishDateUtc, DateTimeKind.Utc).ToLocalTime();
            }
        }

        [Ignore]
        public string RelativeDate => DateHelper.ToRelative(PublishedLocal);

        [Ignore]
        public string LongDate => DateHelper.ToLongText(PublishedLocal);

        [Ignore]
        public double TitleOpacity => IsRead ? 0.6 : 1.0;

        [Ignore]
        public FontAttributes TitleAttributes => IsRead ? FontAttributes.None : FontAttributes.Bold;

        [Ignore]
        public bool ShowUnreadDot => !IsRead;

        [Ignore]
        public bool ShowSummary => !AppSettings.CompactList && !string.IsNullOrWhiteSpace(Summary);

        [Ignore]
        public string ReadingTimeText => ReadingMinutes > 0 ? $"{ReadingMinutes} min" : string.Empty;

        [Ignore]
        public bool HasReadingTime => ReadingMinutes > 0;

        [Ignore]
        public string SiteInitial => string.IsNullOrWhiteSpace(SiteName) ? "•" : SiteName.Trim().Substring(0, 1).ToUpperInvariant();

        [Ignore]
        public ImageSource? SiteIconSource
        {
            get
            {
                if (!string.IsNullOrEmpty(SiteIconPath) && File.Exists(SiteIconPath))
                    return ImageSource.FromFile(SiteIconPath);
                return null;
            }
        }

        [Ignore]
        public bool HasSiteIcon => !string.IsNullOrEmpty(SiteIconPath) && File.Exists(SiteIconPath);

        /// <summary>Vignette : image locale en priorité, sinon distante (si autorisé).</summary>
        [Ignore]
        public ImageSource? ThumbSource
        {
            get
            {
                if (!AppSettings.ShowThumbnails) return null;

                if (!string.IsNullOrEmpty(LocalImagePath) && File.Exists(LocalImagePath))
                    return ImageSource.FromFile(LocalImagePath);

                if (string.IsNullOrWhiteSpace(ImageUrl)) return null;

                // Ancien format : chemin local stocké directement dans ImageUrl
                if (!ImageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    var path = ImageUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ? ImageUrl.Substring(7) : ImageUrl;
                    return File.Exists(path) ? ImageSource.FromFile(path) : null;
                }

                if (AppSettings.DataSaver && Connectivity.Current.NetworkAccess == NetworkAccess.Internet
                    && !Connectivity.Current.ConnectionProfiles.Contains(ConnectionProfile.WiFi))
                    return null;

                return Uri.TryCreate(ImageUrl, UriKind.Absolute, out var uri) ? ImageSource.FromUri(uri) : null;
            }
        }

        [Ignore]
        public bool HasThumb => AppSettings.ShowThumbnails &&
                                (!string.IsNullOrEmpty(LocalImagePath) || !string.IsNullOrWhiteSpace(ImageUrl));

        /// <summary>Conservé de l'ancienne version (couleur de fond selon lu / non lu), adapté au thème.</summary>
        [Ignore]
        public Color BackgroundColor => IsRead ? ThemeService.Palette.Bg : ThemeService.Palette.Surface;

        /// <summary>Conservé : utilise le contenu téléchargé si disponible, sinon le résumé.</summary>
        [Ignore]
        public string PreviewText
        {
            get
            {
                string sourceText = !string.IsNullOrWhiteSpace(ContentHtml)
                    ? HtmlHelper.StripHtml(ContentHtml)
                    : Summary;

                if (string.IsNullOrWhiteSpace(sourceText))
                    return "Aucun aperçu disponible.";

                return HtmlHelper.Truncate(sourceText.Trim(), 1200);
            }
        }

        [Ignore]
        public Uri? LinkUri => Uri.TryCreate(Link, UriKind.Absolute, out var u) ? u : null;

        private void NotifyState()
        {
            Notify(nameof(State), nameof(StatusText), nameof(StatusIcon), nameof(StatusColor),
                   nameof(StatusSoftColor), nameof(IsNotDownloaded), nameof(PreviewText), nameof(IsStored));
        }

        /// <summary>Rafraîchit toutes les propriétés calculées (après changement de thème / réglage).</summary>
        public void RefreshDisplay()
        {
            NotifyState();
            Notify(nameof(ThumbSource), nameof(HasThumb), nameof(ShowSummary), nameof(BackgroundColor),
                   nameof(RelativeDate), nameof(SiteIconSource), nameof(HasSiteIcon));
        }

        /// <summary>Recalcule le texte de recherche normalisé.</summary>
        public void BuildSearchBlob(string? contentText = null)
        {
            var text = $"{Title} {Summary} {SiteName} {Author} {Categories}";
            if (!string.IsNullOrEmpty(contentText))
                text += " " + HtmlHelper.Truncate(contentText, 20000, addEllipsis: false);
            SearchBlob = HtmlHelper.NormalizeForSearch(text);
        }
    }
}
