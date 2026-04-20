// File: Helpers/HtmlHelper.cs

using System.Text.RegularExpressions;
using System.Net; // Utilisé pour décoder les entités HTML

namespace Rss_feeder_prout.Helpers
{
    public static class HtmlHelper
    {
        /// <summary>
        /// Supprime toutes les balises HTML et décode les entités HTML du texte.
        /// </summary>
        public static string StripHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            // 1. Supprime toutes les balises HTML (ex: <div>, <p>, <img>)
            string cleanText = Regex.Replace(html, "<[^>]*>", string.Empty);

            // 2. Décode les entités HTML (ex: &eacute; devient é, &amp; devient &)
            cleanText = WebUtility.HtmlDecode(cleanText);

            return cleanText.Trim();
        }
    }
}