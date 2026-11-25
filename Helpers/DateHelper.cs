using System.Globalization;
using System.Text.RegularExpressions;

namespace Rss_feeder_prout.Helpers
{
    /// <summary>
    /// Analyse robuste des dates des flux (RFC 822 / RFC 1123 / ISO 8601 / formats exotiques)
    /// et formatage "il y a 2 h".
    /// </summary>
    public static class DateHelper
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private static readonly Dictionary<string, string> TimeZones = new(StringComparer.OrdinalIgnoreCase)
        {
            ["UT"] = "+00:00", ["UTC"] = "+00:00", ["GMT"] = "+00:00", ["Z"] = "+00:00",
            ["EST"] = "-05:00", ["EDT"] = "-04:00", ["CST"] = "-06:00", ["CDT"] = "-05:00",
            ["MST"] = "-07:00", ["MDT"] = "-06:00", ["PST"] = "-08:00", ["PDT"] = "-07:00",
            ["AST"] = "-04:00", ["ADT"] = "-03:00", ["NST"] = "-03:30", ["NDT"] = "-02:30",
            ["CET"] = "+01:00", ["CEST"] = "+02:00", ["MET"] = "+01:00", ["MEST"] = "+02:00",
            ["BST"] = "+01:00", ["IST"] = "+05:30", ["JST"] = "+09:00", ["KST"] = "+09:00",
            ["AEST"] = "+10:00", ["AEDT"] = "+11:00", ["ACST"] = "+09:30", ["AWST"] = "+08:00",
            ["NZST"] = "+12:00", ["NZDT"] = "+13:00", ["EET"] = "+02:00", ["EEST"] = "+03:00",
            ["WET"] = "+00:00", ["WEST"] = "+01:00", ["MSK"] = "+03:00", ["HKT"] = "+08:00"
        };

        private static readonly string[] ExactFormats =
        {
            "d MMM yyyy HH:mm:ss zzz", "dd MMM yyyy HH:mm:ss zzz", "d MMM yyyy HH:mm zzz", "dd MMM yyyy HH:mm zzz",
            "d MMM yy HH:mm:ss zzz", "d MMMM yyyy HH:mm:ss zzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
            "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mmzzz", "yyyy-MM-dd HH:mm:ss zzz", "yyyy-MM-dd HH:mm:sszzz",
            "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd", "yyyy/MM/dd HH:mm:ss", "dd/MM/yyyy HH:mm:ss",
            "dd/MM/yyyy HH:mm", "dd/MM/yyyy", "d MMM yyyy HH:mm:ss", "d MMM yyyy"
        };

        /// <summary>Retourne une date UTC, ou null si illisible.</summary>
        public static DateTime? ParseFeedDate(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            string s = raw.Trim();

            // 1. Formats standards directement
            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var dto))
                return Clamp(dto.UtcDateTime);

            // 2. Normalisation : jour de la semaine, fuseaux nommés, "+0000" -> "+00:00"
            s = Regex.Replace(s, @"^[A-Za-zéû\.]{2,9},?\s+", string.Empty);
            s = Regex.Replace(s, @"\s+", " ");
            s = Regex.Replace(s, @"\(.*?\)", string.Empty).Trim();

            var tzMatch = Regex.Match(s, @"\s([A-Za-z]{1,5})$");
            if (tzMatch.Success && TimeZones.TryGetValue(tzMatch.Groups[1].Value, out var offset))
                s = s.Substring(0, tzMatch.Index) + " " + offset;

            s = Regex.Replace(s, @"([+-]\d{2})(\d{2})$", "$1:$2");
            s = s.Replace(" T", "T");

            if (DateTimeOffset.TryParseExact(s, ExactFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out dto))
                return Clamp(dto.UtcDateTime);

            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out dto))
                return Clamp(dto.UtcDateTime);

            // 3. Dates en français ("24 nov. 2026 10:00")
            if (DateTimeOffset.TryParse(s, Fr, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out dto))
                return Clamp(dto.UtcDateTime);

            // 4. Culture courante (anciennes bases : PublishDate stocké en "g")
            if (DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var local))
                return Clamp(local.ToUniversalTime());

            return null;
        }

        /// <summary>Évite les dates dans le futur (flux mal configurés).</summary>
        private static DateTime Clamp(DateTime utc)
        {
            var now = DateTime.UtcNow;
            if (utc > now.AddHours(6)) return now;
            if (utc.Year < 1995) return now;
            return DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        }

        /// <summary>"à l'instant", "il y a 5 min", "il y a 3 h", "hier 14:05", "lun. 12 nov."</summary>
        public static string ToRelative(DateTime local)
        {
            var now = DateTime.Now;
            var diff = now - local;

            if (diff.TotalSeconds < 0) diff = TimeSpan.Zero;
            if (diff.TotalMinutes < 1) return "à l'instant";
            if (diff.TotalMinutes < 60) return $"il y a {(int)diff.TotalMinutes} min";
            if (diff.TotalHours < 24 && local.Date == now.Date) return $"il y a {(int)diff.TotalHours} h";
            if (local.Date == now.Date.AddDays(-1)) return "hier " + local.ToString("HH:mm", Fr);
            if (diff.TotalDays < 7) return local.ToString("dddd HH:mm", Fr);
            if (local.Year == now.Year) return local.ToString("d MMM", Fr);
            return local.ToString("d MMM yyyy", Fr);
        }

        public static string ToLongText(DateTime local) => local.ToString("dddd d MMMM yyyy · HH:mm", Fr);

        public static string TodayText() => Capitalize(DateTime.Now.ToString("dddd d MMMM", Fr));

        public static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0], Fr) + s.Substring(1);

        /// <summary>Formate un délai futur : "dans 2 h 10", "à 18:00", "demain 07:00".</summary>
        public static string ToFuture(DateTime local)
        {
            var now = DateTime.Now;
            var diff = local - now;
            if (diff.TotalMinutes < 1) return "imminente";
            if (diff.TotalMinutes < 60) return $"dans {(int)Math.Ceiling(diff.TotalMinutes)} min";
            if (local.Date == now.Date) return "aujourd'hui à " + local.ToString("HH:mm", Fr);
            if (local.Date == now.Date.AddDays(1)) return "demain à " + local.ToString("HH:mm", Fr);
            return local.ToString("dddd d MMM 'à' HH:mm", Fr);
        }

        public static string Greeting()
        {
            var h = DateTime.Now.Hour;
            if (h < 5) return "Bonne nuit";
            if (h < 12) return "Bonjour";
            if (h < 18) return "Bon après-midi";
            return "Bonsoir";
        }
    }
}
