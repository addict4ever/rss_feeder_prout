using Microsoft.Maui.Storage;
using Rss_feeder_prout.Models;

namespace Rss_feeder_prout.Services
{
    /// <summary>
    /// Tous les réglages de l'application, typés, au même endroit (Preferences).
    /// </summary>
    public static class AppSettings
    {
        private static IPreferences P => Preferences.Default;

        // ------------------------------------------------------------------
        // Synchronisation automatique
        // ------------------------------------------------------------------

        /// <summary>Ancienne clé (OptionRssPage v1) : migrée automatiquement.</summary>
        public const string LegacyIntervalKey = "RssUpdateIntervalMinutes";

        public static bool AutoSyncEnabled
        {
            get
            {
                if (!P.ContainsKey("AutoSyncEnabled") && P.ContainsKey(LegacyIntervalKey))
                    return P.Get(LegacyIntervalKey, 240) > 0;
                return P.Get("AutoSyncEnabled", true);
            }
            set => P.Set("AutoSyncEnabled", value);
        }

        /// <summary>0 = intervalle régulier, 1 = heures fixes.</summary>
        public static int ScheduleMode
        {
            get => P.Get("ScheduleMode", 0);
            set => P.Set("ScheduleMode", value);
        }

        public static int IntervalMinutes
        {
            get
            {
                if (!P.ContainsKey("IntervalMinutes") && P.ContainsKey(LegacyIntervalKey))
                {
                    var legacy = P.Get(LegacyIntervalKey, 240);
                    return legacy > 0 ? legacy : 240;
                }
                return P.Get("IntervalMinutes", 240);
            }
            set => P.Set("IntervalMinutes", Math.Max(5, value));
        }

        /// <summary>Heures fixes au format "07:00;12:30;18:00".</summary>
        public static string FixedTimes
        {
            get => P.Get("FixedTimes", "07:00;12:00;18:00");
            set => P.Set("FixedTimes", value ?? string.Empty);
        }

        public static List<TimeSpan> GetFixedTimes()
        {
            var list = new List<TimeSpan>();
            foreach (var part in FixedTimes.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                if (TimeSpan.TryParse(part.Trim(), out var ts) && ts >= TimeSpan.Zero && ts < TimeSpan.FromDays(1))
                    list.Add(new TimeSpan(ts.Hours, ts.Minutes, 0));
            }
            return list.Distinct().OrderBy(t => t).ToList();
        }

        public static void SetFixedTimes(IEnumerable<TimeSpan> times)
        {
            FixedTimes = string.Join(";", times.Distinct().OrderBy(t => t).Select(t => $"{t.Hours:00}:{t.Minutes:00}"));
        }

        public static bool WifiOnly
        {
            get => P.Get("WifiOnly", false);
            set => P.Set("WifiOnly", value);
        }

        public static bool ChargingOnly
        {
            get => P.Get("ChargingOnly", false);
            set => P.Set("ChargingOnly", value);
        }

        public static bool AutoDownloadContent
        {
            get => P.Get("AutoDownloadContent", true);
            set => P.Set("AutoDownloadContent", value);
        }

        public static ImageMode AutoImageMode
        {
            get => (ImageMode)P.Get("AutoImageMode", (int)ImageMode.Optimized);
            set => P.Set("AutoImageMode", (int)value);
        }

        public static bool NotifyOnSync
        {
            get => P.Get("NotifyOnSync", true);
            set => P.Set("NotifyOnSync", value);
        }

        public static DateTime LastSyncUtc
        {
            get => P.Get("LastSyncUtc", DateTime.MinValue);
            set => P.Set("LastSyncUtc", value);
        }

        public static DateTime LastAutoSyncUtc
        {
            get => P.Get("LastAutoSyncUtc", DateTime.MinValue);
            set => P.Set("LastAutoSyncUtc", value);
        }

        public static DateTime NextSyncUtc
        {
            get => P.Get("NextSyncUtc", DateTime.MinValue);
            set => P.Set("NextSyncUtc", value);
        }

        // ------------------------------------------------------------------
        // Synchronisation manuelle (choix proposés)
        // ------------------------------------------------------------------

        public static bool ManualDownloadContent
        {
            get => P.Get("ManualDownloadContent", true);
            set => P.Set("ManualDownloadContent", value);
        }

        public static ImageMode ManualImageMode
        {
            get => (ImageMode)P.Get("ManualImageMode", (int)ImageMode.Optimized);
            set => P.Set("ManualImageMode", (int)value);
        }

        public static bool RememberSyncChoice
        {
            get => P.Get("RememberSyncChoice", false);
            set => P.Set("RememberSyncChoice", value);
        }

        public static int MaxArticlesPerSync
        {
            get => P.Get("MaxArticlesPerSync", 300);
            set => P.Set("MaxArticlesPerSync", Math.Clamp(value, 20, 5000));
        }

        // ------------------------------------------------------------------
        // Images
        // ------------------------------------------------------------------

        public static int ImageMaxWidth
        {
            get => P.Get("ImageMaxWidth", 1080);
            set => P.Set("ImageMaxWidth", Math.Clamp(value, 320, 4000));
        }

        public static int ImageQuality
        {
            get => P.Get("ImageQuality", 72);
            set => P.Set("ImageQuality", Math.Clamp(value, 30, 100));
        }

        /// <summary>Utiliser le proxy d'images Google (méthode anti-hotlink d'origine) en secours.</summary>
        /// <summary>
        /// Méthodes anti-paywall / anti-blocage (client furtif, navigation profonde, contournement structurel,
        /// retrait des bannières, proxy d'images Google). DÉSACTIVÉES par défaut : le code est conservé.
        /// </summary>
        public static bool UsePaywallMethods
        {
            get => P.Get("UsePaywallMethods", false);
            set => P.Set("UsePaywallMethods", value);
        }

        public static bool UseGoogleImageProxy
        {
            get => UsePaywallMethods && P.Get("UseGoogleImageProxy", false);
            set => P.Set("UseGoogleImageProxy", value);
        }

        public static bool ShowThumbnails
        {
            get => P.Get("ShowThumbnails", true);
            set => P.Set("ShowThumbnails", value);
        }

        /// <summary>Économie de données : pas de vignettes distantes hors Wi-Fi.</summary>
        public static bool DataSaver
        {
            get => P.Get("DataSaver", false);
            set => P.Set("DataSaver", value);
        }

        // ------------------------------------------------------------------
        // Apparence & lecture
        // ------------------------------------------------------------------

        /// <summary>"dark", "light" ou "system".</summary>
        public static string AppTheme
        {
            get => P.Get("AppTheme", "dark");
            set => P.Set("AppTheme", value ?? "dark");
        }

        public static bool CompactList
        {
            get => P.Get("CompactList", false);
            set => P.Set("CompactList", value);
        }

        public static int ReaderFontSize
        {
            get => P.Get("ReaderFontSize", 18);
            set => P.Set("ReaderFontSize", Math.Clamp(value, 12, 32));
        }

        /// <summary>"app", "dark", "light", "sepia", "black".</summary>
        public static string ReaderTheme
        {
            get => P.Get("ReaderTheme", "app");
            set => P.Set("ReaderTheme", value ?? "app");
        }

        public static bool ReaderSerif
        {
            get => P.Get("ReaderSerif", false);
            set => P.Set("ReaderSerif", value);
        }

        public static double ReaderLineHeight
        {
            get => P.Get("ReaderLineHeight", 1.65);
            set => P.Set("ReaderLineHeight", Math.Clamp(value, 1.2, 2.2));
        }

        public static bool ReaderJustify
        {
            get => P.Get("ReaderJustify", false);
            set => P.Set("ReaderJustify", value);
        }

        public static bool MarkReadOnOpen
        {
            get => P.Get("MarkReadOnOpen", true);
            set => P.Set("MarkReadOnOpen", value);
        }

        /// <summary>Récupérer l'article complet à l'ouverture s'il n'est pas hors-ligne.</summary>
        public static bool AutoExtractOnOpen
        {
            get => P.Get("AutoExtractOnOpen", true);
            set => P.Set("AutoExtractOnOpen", value);
        }

        /// <summary>0 = lecteur intégré, 1 = navigateur.</summary>
        public static int OpenMode
        {
            get => P.Get("OpenMode", 0);
            set => P.Set("OpenMode", value);
        }

        /// <summary>Afficher par défaut les articles en ligne non enregistrés.</summary>
        public static bool ShowOnlineByDefault
        {
            get => P.Get("ShowOnlineByDefault", true);
            set => P.Set("ShowOnlineByDefault", value);
        }

        public static bool ShowReadArticles
        {
            get => P.Get("ShowReadArticles", true);
            set => P.Set("ShowReadArticles", value);
        }

        // ------------------------------------------------------------------
        // Rétention / nettoyage automatique
        // ------------------------------------------------------------------

        /// <summary>Supprimer les articles LUS de plus de N jours (0 = jamais).</summary>
        public static int RetentionReadDays
        {
            get => P.Get("RetentionReadDays", 30);
            set => P.Set("RetentionReadDays", Math.Max(0, value));
        }

        /// <summary>Supprimer TOUS les articles de plus de N jours (0 = jamais). Favoris épargnés.</summary>
        public static int RetentionAllDays
        {
            get => P.Get("RetentionAllDays", 0);
            set => P.Set("RetentionAllDays", Math.Max(0, value));
        }

        // ------------------------------------------------------------------
        // Filtres mots-clés
        // ------------------------------------------------------------------

        public static string MutedKeywords
        {
            get => P.Get("MutedKeywords", string.Empty);
            set => P.Set("MutedKeywords", value ?? string.Empty);
        }

        public static string HighlightKeywords
        {
            get => P.Get("HighlightKeywords", string.Empty);
            set => P.Set("HighlightKeywords", value ?? string.Empty);
        }

        // ------------------------------------------------------------------
        // Sauvegardes
        // ------------------------------------------------------------------

        public static bool AutoBackupEnabled
        {
            get => P.Get("AutoBackupEnabled", true);
            set => P.Set("AutoBackupEnabled", value);
        }

        public static int AutoBackupDays
        {
            get => P.Get("AutoBackupDays", 7);
            set => P.Set("AutoBackupDays", Math.Clamp(value, 1, 90));
        }

        public static DateTime LastAutoBackupUtc
        {
            get => P.Get("LastAutoBackupUtc", DateTime.MinValue);
            set => P.Set("LastAutoBackupUtc", value);
        }

        public static int BackupKeepCount
        {
            get => P.Get("BackupKeepCount", 5);
            set => P.Set("BackupKeepCount", Math.Clamp(value, 1, 50));
        }

        // ------------------------------------------------------------------
        // Divers
        // ------------------------------------------------------------------

        public static string RecentSearches
        {
            get => P.Get("RecentSearches", string.Empty);
            set => P.Set("RecentSearches", value ?? string.Empty);
        }

        public static void AddRecentSearch(string term)
        {
            if (string.IsNullOrWhiteSpace(term)) return;
            var list = RecentSearches.Split('\u001F', StringSplitOptions.RemoveEmptyEntries).ToList();
            list.RemoveAll(s => string.Equals(s, term.Trim(), StringComparison.OrdinalIgnoreCase));
            list.Insert(0, term.Trim());
            RecentSearches = string.Join('\u001F', list.Take(8));
        }

        public static List<string> GetRecentSearches() =>
            RecentSearches.Split('\u001F', StringSplitOptions.RemoveEmptyEntries).ToList();

        // ------------------------------------------------------------------
        // Export / import des réglages (inclus dans les sauvegardes)
        // ------------------------------------------------------------------

        private static readonly string[] IntKeys =
        {
            "ScheduleMode", "IntervalMinutes", "AutoImageMode", "ManualImageMode", "MaxArticlesPerSync",
            "ImageMaxWidth", "ImageQuality", "ReaderFontSize", "OpenMode", "RetentionReadDays",
            "RetentionAllDays", "AutoBackupDays", "BackupKeepCount"
        };

        private static readonly string[] BoolKeys =
        {
            "AutoSyncEnabled", "WifiOnly", "ChargingOnly", "AutoDownloadContent", "NotifyOnSync",
            "ManualDownloadContent", "RememberSyncChoice", "UseGoogleImageProxy", "UsePaywallMethods", "ShowThumbnails", "DataSaver",
            "CompactList", "ReaderSerif", "ReaderJustify", "MarkReadOnOpen", "AutoExtractOnOpen",
            "ShowOnlineByDefault", "ShowReadArticles", "AutoBackupEnabled"
        };

        private static readonly string[] StringKeys =
        {
            "FixedTimes", "AppTheme", "ReaderTheme", "MutedKeywords", "HighlightKeywords"
        };

        public static Dictionary<string, string> Export()
        {
            var d = new Dictionary<string, string>();
            foreach (var k in IntKeys) if (P.ContainsKey(k)) d[k] = P.Get(k, 0).ToString();
            foreach (var k in BoolKeys) if (P.ContainsKey(k)) d[k] = P.Get(k, false) ? "1" : "0";
            foreach (var k in StringKeys) if (P.ContainsKey(k)) d[k] = P.Get(k, string.Empty);
            if (P.ContainsKey("ReaderLineHeight")) d["ReaderLineHeight"] = P.Get("ReaderLineHeight", 1.65).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return d;
        }

        public static void Import(IDictionary<string, string> values)
        {
            foreach (var (k, v) in values)
            {
                if (IntKeys.Contains(k) && int.TryParse(v, out var i)) P.Set(k, i);
                else if (BoolKeys.Contains(k)) P.Set(k, v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
                else if (StringKeys.Contains(k)) P.Set(k, v ?? string.Empty);
                else if (k == "ReaderLineHeight" && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dbl)) P.Set(k, dbl);
            }
        }
    }
}
