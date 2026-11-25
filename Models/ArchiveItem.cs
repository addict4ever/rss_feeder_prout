using SQLite;
using Rss_feeder_prout.Helpers;
using Microsoft.Maui.Controls;

namespace Rss_feeder_prout.Models
{
    /// <summary>
    /// Article archivé "pour toujours". Stocké dans Databases/RssArchives.db3,
    /// indépendant du nettoyage automatique des articles.
    /// </summary>
    [Table("Archives")]
    public class ArchiveItem : ObservableModel
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string PublishDate { get; set; } = string.Empty;
        public DateTime PublishDateUtc { get; set; }

        [Indexed]
        public string ArticleGuid { get; set; } = string.Empty;
        public string Link { get; set; } = string.Empty;

        /// <summary>Date à laquelle l'article a été archivé.</summary>
        public DateTime ArchivedAt { get; set; } = DateTime.Now;

        public string Author { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string LocalImagePath { get; set; } = string.Empty;

        [MaxLength(25000)]
        public string ContentHtml { get; set; } = string.Empty;

        public int? SiteId { get; set; }

        private string _siteName = string.Empty;
        /// <summary>Maintenant stocké (avant : [Ignore], donc perdu).</summary>
        public string SiteName
        {
            get => _siteName;
            set => SetProperty(ref _siteName, value ?? string.Empty);
        }

        public int ReadingMinutes { get; set; }

        private string _note = string.Empty;
        /// <summary>NOUVEAU : note personnelle sur l'article archivé.</summary>
        public string Note
        {
            get => _note;
            set
            {
                if (SetProperty(ref _note, value ?? string.Empty))
                    OnPropertyChanged(nameof(HasNote));
            }
        }

        [Ignore]
        public string PreviewText => HtmlHelper.Truncate(
            !string.IsNullOrWhiteSpace(Summary) ? HtmlHelper.StripHtml(Summary) : HtmlHelper.StripHtml(ContentHtml), 300);

        [Ignore] public bool HasNote => !string.IsNullOrWhiteSpace(Note);

        [Ignore] public string ArchivedText => "Archivé " + DateHelper.ToRelative(ArchivedAt);

        [Ignore]
        public ImageSource? ThumbSource
        {
            get
            {
                if (!string.IsNullOrEmpty(LocalImagePath) && File.Exists(LocalImagePath))
                    return ImageSource.FromFile(LocalImagePath);
                if (Uri.TryCreate(ImageUrl, UriKind.Absolute, out var u) && u.Scheme.StartsWith("http"))
                    return ImageSource.FromUri(u);
                return null;
            }
        }

        [Ignore] public bool HasThumb => !string.IsNullOrEmpty(LocalImagePath) || !string.IsNullOrWhiteSpace(ImageUrl);

        [Ignore]
        public string SiteInitial => string.IsNullOrWhiteSpace(SiteName) ? "A" : SiteName.Trim().Substring(0, 1).ToUpperInvariant();

        /// <summary>Crée une archive à partir d'un article.</summary>
        public static ArchiveItem FromArticle(RssItem a) => new ArchiveItem
        {
            Title = a.Title,
            Summary = a.Summary,
            PublishDate = a.PublishDate,
            PublishDateUtc = a.PublishDateUtc,
            ArticleGuid = a.ArticleGuid,
            Link = a.Link,
            Author = a.Author,
            ImageUrl = a.ImageUrl,
            LocalImagePath = a.LocalImagePath,
            ContentHtml = a.ContentHtml,
            SiteId = a.SiteId,
            SiteName = a.SiteName,
            ReadingMinutes = a.ReadingMinutes,
            ArchivedAt = DateTime.Now
        };

        /// <summary>Convertit l'archive en RssItem pour l'afficher dans le lecteur.</summary>
        public RssItem ToRssItem() => new RssItem
        {
            Id = Id,
            Title = Title,
            Summary = Summary,
            ContentHtml = ContentHtml,
            ImageUrl = ImageUrl,
            LocalImagePath = LocalImagePath,
            SiteName = SiteName,
            SiteId = SiteId,
            Link = Link,
            Author = Author,
            PublishDate = PublishDate,
            PublishDateUtc = PublishDateUtc == default ? DateTime.SpecifyKind(ArchivedAt, DateTimeKind.Local).ToUniversalTime() : PublishDateUtc,
            ArticleGuid = ArticleGuid,
            ReadingMinutes = ReadingMinutes,
            IsDownloaded = !string.IsNullOrWhiteSpace(ContentHtml),
            IsRead = true
        };
    }
}
