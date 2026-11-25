using SQLite;
using Rss_feeder_prout.Helpers;

namespace Rss_feeder_prout.Models
{
    // =====================================================================
    // Requêtes d'articles
    // =====================================================================

    public enum ArticleFilter
    {
        All,
        Unread,
        Read,
        Favorites,
        ReadLater,
        Downloaded,
        NotDownloaded
    }

    public enum ArticleSort
    {
        Newest,
        Oldest,
        BySite
    }

    /// <summary>Flux "intelligents" transversaux (toutes catégories confondues).</summary>
    public enum SmartFeed
    {
        None,
        All,
        Unread,
        Today,
        Favorites,
        ReadLater,
        Downloaded
    }

    /// <summary>Description d'une requête sur la base d'articles.</summary>
    public class ArticleQuery
    {
        public List<int>? PlaylistIds { get; set; }
        public int? PlaylistId { get; set; }
        public int? SiteId { get; set; }
        public ArticleFilter Filter { get; set; } = ArticleFilter.All;
        public DateTime? SinceUtc { get; set; }
        public string? Search { get; set; }
        public bool SearchTitleOnly { get; set; }
        public ArticleSort Sort { get; set; } = ArticleSort.Newest;
        public int Limit { get; set; } = 200;
        public int Offset { get; set; }

        public ArticleQuery Clone() => (ArticleQuery)MemberwiseClone();
    }

    public class SmartCounts
    {
        public int All { get; set; }
        public int Unread { get; set; }
        public int Today { get; set; }
        public int Favorites { get; set; }
        public int ReadLater { get; set; }
        public int Downloaded { get; set; }
    }

    /// <summary>Ligne générique "clé / valeur" pour les requêtes GROUP BY.</summary>
    public class CountRow
    {
        public int K { get; set; }
        public int V { get; set; }
    }

    public class NamedCountRow
    {
        public string N { get; set; } = string.Empty;
        public int V { get; set; }
    }

    /// <summary>Élément de statistique affiché avec une barre.</summary>
    public class NamedCount
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
        public double Ratio { get; set; }
        public string CountText => Count.ToString("N0");
    }

    // =====================================================================
    // Synchronisation
    // =====================================================================

    /// <summary>Que faire des images lors d'un téléchargement complet ?</summary>
    public enum ImageMode
    {
        /// <summary>Ne pas télécharger les images (texte seulement, images en ligne).</summary>
        None = 0,
        /// <summary>Télécharger les images telles quelles.</summary>
        Original = 1,
        /// <summary>Télécharger, redimensionner et compresser (JPEG).</summary>
        Optimized = 2
    }

    public enum SyncTrigger
    {
        Manual,
        Auto,
        Background,
        Pull
    }

    public class SyncRequest
    {
        /// <summary>null = toutes les catégories actives.</summary>
        public IReadOnlyList<int>? PlaylistIds { get; set; }
        public bool DownloadContent { get; set; }
        public ImageMode ImageMode { get; set; } = ImageMode.Optimized;
        public SyncTrigger Trigger { get; set; } = SyncTrigger.Manual;
        public bool OnlyActivePlaylists { get; set; } = true;
        public int MaxArticles { get; set; } = 300;
        public string Label { get; set; } = "Synchronisation";
    }

    public class SyncResult
    {
        public bool Completed { get; set; }
        public bool Skipped { get; set; }
        public bool Cancelled { get; set; }
        public int NewItems { get; set; }
        public int DownloadedItems { get; set; }
        public int ImagesSaved { get; set; }
        public int TotalFeeds { get; set; }
        public int FailedFeeds { get; set; }
        public int CleanedItems { get; set; }
        public TimeSpan Duration { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<string> Errors { get; } = new();

        public string Summary
        {
            get
            {
                if (Skipped) return Message;
                var parts = new List<string>
                {
                    NewItems == 1 ? "1 nouvel article" : $"{NewItems} nouveaux articles"
                };
                if (DownloadedItems > 0) parts.Add($"{DownloadedItems} hors-ligne");
                if (FailedFeeds > 0) parts.Add($"{FailedFeeds} flux en erreur");
                if (Cancelled) parts.Add("annulée");
                return string.Join(" · ", parts);
            }
        }
    }

    /// <summary>Journal des synchronisations (Databases/RssConfig.db3).</summary>
    public class SyncLogEntry
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        [Indexed]
        public DateTime StartedUtc { get; set; }
        public double DurationSeconds { get; set; }
        public string Trigger { get; set; } = string.Empty;
        public int TotalFeeds { get; set; }
        public int FailedFeeds { get; set; }
        public int NewItems { get; set; }
        public int DownloadedItems { get; set; }
        public bool Cancelled { get; set; }
        public string Message { get; set; } = string.Empty;

        [Ignore]
        public string WhenText => DateHelper.ToLongText(DateTime.SpecifyKind(StartedUtc, DateTimeKind.Utc).ToLocalTime());

        [Ignore]
        public string TriggerText => Trigger switch
        {
            nameof(SyncTrigger.Auto) => "Automatique",
            nameof(SyncTrigger.Background) => "Arrière-plan",
            nameof(SyncTrigger.Pull) => "Actualisation",
            _ => "Manuelle"
        };

        [Ignore]
        public string DetailText
        {
            get
            {
                var s = $"{NewItems} nouveaux · {DownloadedItems} hors-ligne · {TotalFeeds - FailedFeeds}/{TotalFeeds} flux OK · {DurationSeconds:0}s";
                if (Cancelled) s += " · annulée";
                return s;
            }
        }

        [Ignore] public bool HasErrors => FailedFeeds > 0;
    }

    // =====================================================================
    // Analyse de flux
    // =====================================================================

    public class ParsedFeed
    {
        public string Title { get; set; } = string.Empty;
        public string SiteLink { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string IconUrl { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public List<ParsedFeedItem> Items { get; } = new();
    }

    public class ParsedFeedItem
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Link { get; set; } = string.Empty;
        public string SummaryHtml { get; set; } = string.Empty;
        public string ContentHtml { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public DateTime? PublishedUtc { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public List<string> Categories { get; } = new();
    }

    public class FeedFetchResult
    {
        public bool Success { get; set; }
        public bool NotModified { get; set; }
        public ParsedFeed? Feed { get; set; }
        public string Error { get; set; } = string.Empty;
        public string ETag { get; set; } = string.Empty;
        public string LastModified { get; set; } = string.Empty;
        public int StatusCode { get; set; }
        public string FinalUrl { get; set; } = string.Empty;
    }

    public class DiscoveredFeed
    {
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public override string ToString() => string.IsNullOrWhiteSpace(Title) ? Url : $"{Title} ({Url})";
    }

    // =====================================================================
    // Stockage / sauvegardes
    // =====================================================================

    public class DbFileInfo
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTime Modified { get; set; }
        public string Kind { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string SizeText => FormatHelper.Bytes(SizeBytes);
        public string DateText => Modified.ToString("dd/MM/yyyy HH:mm");
        public string Subtitle => string.IsNullOrEmpty(Description) ? $"{SizeText} · {DateText}" : $"{Description} · {SizeText}";
        public bool IsZip => FullPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }

    public class BackupInfo
    {
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTime CreatedLocal { get; set; }
        public bool IsAuto { get; set; }
        public bool IsSafety { get; set; }
        public string SizeText => FormatHelper.Bytes(SizeBytes);
        public string DateText => CreatedLocal.ToString("dddd d MMMM yyyy · HH:mm");
        public string Label => IsSafety ? "Avant restauration" : (IsAuto ? "Automatique" : "Manuelle");
    }

    public class StorageReport
    {
        public long ConfigBytes { get; set; }
        public long ArticlesBytes { get; set; }
        public long ArchivesBytes { get; set; }
        public long ImageCacheBytes { get; set; }
        public int ImageCacheCount { get; set; }
        public long IconsBytes { get; set; }
        public long BackupsBytes { get; set; }
        public int BackupCount { get; set; }
        public long ColdArchiveBytes { get; set; }
        public int ColdArchiveCount { get; set; }
        public long LegacyBytes { get; set; }
        public int LegacyCount { get; set; }
        public int ArticleCount { get; set; }
        public int DownloadedCount { get; set; }
        public int ArchiveCount { get; set; }
        public int SiteCount { get; set; }
        public int PlaylistCount { get; set; }

        public long DatabasesBytes => ConfigBytes + ArticlesBytes + ArchivesBytes;
        public long TotalBytes => DatabasesBytes + ImageCacheBytes + IconsBytes + BackupsBytes + ColdArchiveBytes + LegacyBytes;
    }

    public class BackupOptions
    {
        public bool IncludeArticles { get; set; } = true;
        public bool IncludeArchives { get; set; } = true;
        public bool IncludeImages { get; set; }
        public bool IncludeSettings { get; set; } = true;
        public System.IO.Compression.CompressionLevel Level { get; set; } = System.IO.Compression.CompressionLevel.Optimal;
        public bool IsAuto { get; set; }
        public bool IsSafety { get; set; }
    }

    public class RestoreOptions
    {
        public bool RestoreConfig { get; set; } = true;
        public bool RestoreArticles { get; set; } = true;
        public bool RestoreArchives { get; set; } = true;
        public bool RestoreImages { get; set; } = true;
        public bool RestoreSettings { get; set; } = true;
        /// <summary>true = fusionner les articles au lieu de remplacer la base.</summary>
        public bool MergeArticles { get; set; }
    }
}
