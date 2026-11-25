using SQLite;
using Microsoft.Maui.Graphics;

namespace Rss_feeder_prout.Models
{
    /// <summary>Catégorie (groupe de sources). Stockée dans Databases/RssConfig.db3.</summary>
    public class FeedPlaylist : ObservableModel
    {
        /// <summary>Palette proposée pour colorer les catégories.</summary>
        public static readonly string[] ColorChoices =
        {
            "#FF8A3D", "#5AA9FF", "#3DD68C", "#F5B544", "#B48CFF", "#FF6B8B", "#2EC4C9", "#A3A9B6"
        };

        public static readonly string[] ColorNames =
        {
            "Orange", "Bleu", "Vert", "Ambre", "Violet", "Rose", "Turquoise", "Gris"
        };

        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value ?? string.Empty);
        }

        private bool _isActive = true;
        /// <summary>Incluse dans "Tout synchroniser" et la synchro automatique.</summary>
        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (SetProperty(ref _isActive, value))
                    OnPropertyChanged(nameof(SubtitleText));
            }
        }

        public int SortOrder { get; set; }

        private string _colorHex = string.Empty;
        public string ColorHex
        {
            get => _colorHex;
            set
            {
                if (SetProperty(ref _colorHex, value ?? string.Empty))
                    OnPropertyChanged(nameof(AccentColor));
            }
        }

        // ------------------------------------------------------------------
        // UI (non stocké)
        // ------------------------------------------------------------------

        private int _siteCount;
        [Ignore]
        public int SiteCount
        {
            get => _siteCount;
            set
            {
                if (SetProperty(ref _siteCount, value))
                    OnPropertyChanged(nameof(SubtitleText));
            }
        }

        private int _unreadCount;
        [Ignore]
        public int UnreadCount
        {
            get => _unreadCount;
            set
            {
                if (SetProperty(ref _unreadCount, value))
                    Notify(nameof(SubtitleText), nameof(HasUnread), nameof(UnreadText));
            }
        }

        private int _totalCount;
        [Ignore]
        public int TotalCount
        {
            get => _totalCount;
            set => SetProperty(ref _totalCount, value);
        }

        [Ignore] public bool HasUnread => UnreadCount > 0;
        [Ignore] public string UnreadText => UnreadCount > 999 ? "999+" : UnreadCount.ToString();

        [Ignore]
        public string SubtitleText
        {
            get
            {
                var sources = SiteCount == 1 ? "1 source" : $"{SiteCount} sources";
                var unread = UnreadCount == 0 ? "tout est lu" : (UnreadCount == 1 ? "1 non lu" : $"{UnreadCount} non lus");
                var paused = IsActive ? string.Empty : " · synchro en pause";
                return $"{sources} · {unread}{paused}";
            }
        }

        [Ignore]
        public Color AccentColor
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ColorHex))
                {
                    try { return Color.FromArgb(ColorHex); } catch { }
                }
                var idx = Math.Abs(Id) % ColorChoices.Length;
                return Color.FromArgb(ColorChoices[idx]);
            }
        }

        [Ignore]
        public string Initial => string.IsNullOrWhiteSpace(Name) ? "#" : Name.Trim().Substring(0, 1).ToUpperInvariant();
    }
}
