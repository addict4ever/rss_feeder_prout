// File: Helpers/HtmlHelper.cs

using System.Globalization;
using System.Net; // Utilisé pour décoder les entités HTML
using System.Text;
using System.Text.RegularExpressions;

namespace Rss_feeder_prout.Helpers
{
    public static class HtmlHelper
    {
        private static readonly Regex ScriptStyleRegex = new(@"<(script|style|noscript)\b[^>]*>[\s\S]*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BlockTagRegex = new(@"<\s*(br|/p|/div|/li|/h[1-6]|/blockquote|/tr)\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TagRegex = new("<[^>]*>", RegexOptions.Compiled);
        private static readonly Regex SpacesRegex = new(@"[ \t\f\v]+", RegexOptions.Compiled);
        private static readonly Regex NewLinesRegex = new(@"\s*\n\s*", RegexOptions.Compiled);

        /// <summary>
        /// Supprime toutes les balises HTML et décode les entités HTML du texte.
        /// (Méthode d'origine améliorée : scripts/styles retirés, sauts de ligne conservés.)
        /// </summary>
        public static string StripHtml(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;

            string clean = ScriptStyleRegex.Replace(html, " ");
            clean = BlockTagRegex.Replace(clean, "\n");
            clean = TagRegex.Replace(clean, " ");
            clean = WebUtility.HtmlDecode(clean);
            clean = SpacesRegex.Replace(clean, " ");
            clean = NewLinesRegex.Replace(clean, "\n");
            return clean.Trim();
        }

        /// <summary>Texte d'une seule ligne (résumés).</summary>
        public static string StripHtmlSingleLine(string? html)
        {
            var text = StripHtml(html);
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        /// <summary>Supprime les accents (É -> E). Reprise de MainViewModel.RemoveAccents.</summary>
        public static string RemoveAccents(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;

            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);
            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>Minuscules + sans accents + espaces simples : base de la recherche.</summary>
        public static string NormalizeForSearch(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var s = RemoveAccents(text).ToLowerInvariant();
            return Regex.Replace(s, @"\s+", " ").Trim();
        }

        /// <summary>Découpe une saisie "mot1, mot2 ; mot3" en mots-clés normalisés.</summary>
        public static List<string> ParseKeywords(string? csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return new List<string>();
            return csv.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                      .Select(k => NormalizeForSearch(k))
                      .Where(k => k.Length > 1)
                      .Distinct()
                      .ToList();
        }

        public static bool ContainsAny(string normalizedText, IReadOnlyList<string> normalizedKeywords)
        {
            if (normalizedKeywords.Count == 0 || string.IsNullOrEmpty(normalizedText)) return false;
            foreach (var k in normalizedKeywords)
            {
                if (normalizedText.Contains(k, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>Temps de lecture estimé (230 mots / minute).</summary>
        public static int EstimateReadingMinutes(string? html)
        {
            var text = StripHtml(html);
            if (text.Length == 0) return 0;
            int words = Regex.Matches(text, @"\w+").Count;
            return Math.Max(1, (int)Math.Round(words / 230.0));
        }

        /// <summary>Coupe proprement un texte sans casser un mot.</summary>
        public static string Truncate(string? text, int maxLength, bool addEllipsis = true)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.Length <= maxLength) return text;

            var cut = text.Substring(0, maxLength);
            int lastSpace = cut.LastIndexOf(' ');
            if (lastSpace > maxLength * 0.6) cut = cut.Substring(0, lastSpace);
            return addEllipsis ? cut.TrimEnd() + "…" : cut;
        }

        public static string Encode(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

        /// <summary>Longueur de texte "réel" d'un HTML (sert à détecter un teaser).</summary>
        public static int TextLength(string? html) => StripHtml(html).Length;
    }
}
