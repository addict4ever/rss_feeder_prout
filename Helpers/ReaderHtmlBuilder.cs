using Rss_feeder_prout.Models;
using Rss_feeder_prout.Services;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Rss_feeder_prout.Helpers
{
    /// <summary>
    /// Construit la page HTML du lecteur (mode lecture) : typographie soignée, thème,
    /// taille de police, en-tête (site, titre, auteur, date, temps de lecture), image principale,
    /// images locales converties en data URI (fonctionne hors-ligne dans tous les WebView).
    /// </summary>
    public static class ReaderHtmlBuilder
    {
        private static readonly Regex LocalImgRegex = new(@"src=([""'])file://([^""']+)\1", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Au-delà, le WebView Android peut manquer de mémoire et faire planter l'app.</summary>
        private const int MaxBodyChars = 600_000;

        /// <summary>
        /// BaseUrl du lecteur : le dossier des images. Les images hors-ligne sont chargées directement
        /// depuis les fichiers (file://) au lieu d'être converties en data URI (des Mo de texte = plantage).
        /// </summary>
        public static string ReaderBaseUrl => "file://" + SQLiteService.ImageCacheFolder.TrimEnd('/') + "/";

        public static Task<string> BuildAsync(RssItem item, string bodyHtml, string? notice = null, IReadOnlyList<string>? highlight = null)
        {
            var (bg, fg, muted, link, card, border) = GetColors();
            int size = AppSettings.ReaderFontSize;
            string lh = AppSettings.ReaderLineHeight.ToString("0.00", CultureInfo.InvariantCulture);
            string family = AppSettings.ReaderSerif
                ? "Georgia, 'Noto Serif', 'Times New Roman', serif"
                : "-apple-system, 'Segoe UI', Roboto, 'Open Sans', 'Helvetica Neue', Arial, sans-serif";
            string align = AppSettings.ReaderJustify ? "justify" : "left";

            bodyHtml ??= string.Empty;
            if (bodyHtml.Length > MaxBodyChars)
                bodyHtml = HtmlHelper.Truncate(bodyHtml, MaxBodyChars, addEllipsis: false) + "<p><em>(Article tronqué : trop long pour le lecteur. Ouvrez-le dans le navigateur pour la suite.)</em></p>";
            bodyHtml = RemoveDuplicateTitle(bodyHtml, item.Title);
            bodyHtml = FixLocalImages(bodyHtml);

            // Image principale (si le contenu n'en contient pas déjà)
            string hero = string.Empty;
            if (!bodyHtml.Contains("<img", StringComparison.OrdinalIgnoreCase))
            {
                string? heroSrc = null;
                if (!string.IsNullOrEmpty(item.LocalImagePath) && File.Exists(item.LocalImagePath))
                    heroSrc = "file://" + item.LocalImagePath;
                else if (!string.IsNullOrWhiteSpace(item.ImageUrl) && item.ImageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                         && Microsoft.Maui.Networking.Connectivity.Current.NetworkAccess == Microsoft.Maui.Networking.NetworkAccess.Internet)
                    heroSrc = item.ImageUrl;

                if (!string.IsNullOrEmpty(heroSrc))
                    hero = $"<img class='hero' src='{HtmlHelper.Encode(heroSrc)}' alt=''/>";
            }

            var meta = new List<string>();
            if (!string.IsNullOrWhiteSpace(item.Author)) meta.Add(HtmlHelper.Encode(item.Author));
            meta.Add(HtmlHelper.Encode(item.LongDate));
            int minutes = item.ReadingMinutes > 0 ? item.ReadingMinutes : HtmlHelper.EstimateReadingMinutes(bodyHtml);
            if (minutes > 0) meta.Add($"{minutes} min de lecture");

            string status = item.State switch
            {
                ArticleState.Online => "<span class='pill online'>En ligne · non enregistré</span>",
                ArticleState.Offline => "<span class='pill offline'>Disponible hors-ligne</span>",
                _ => "<span class='pill cached'>Résumé enregistré</span>"
            };

            if (highlight != null && highlight.Count > 0)
                bodyHtml = Highlight(bodyHtml, highlight);

            var sb = new StringBuilder(bodyHtml.Length + 4096);
            sb.Append("<!DOCTYPE html><html lang='fr'><head><meta charset='utf-8'>");
            sb.Append("<meta name='viewport' content='width=device-width, initial-scale=1, maximum-scale=4'>");
            sb.Append("<style>");
            sb.Append($":root{{--bg:{bg};--fg:{fg};--muted:{muted};--link:{link};--card:{card};--border:{border};}}");
            sb.Append("*{box-sizing:border-box}");
            sb.Append($"html,body{{margin:0;padding:0;background:var(--bg);color:var(--fg);}}");
            sb.Append($"body{{font-family:{family};font-size:{size}px;line-height:{lh};-webkit-text-size-adjust:100%;word-wrap:break-word;overflow-wrap:anywhere;}}");
            sb.Append(".wrap{max-width:720px;margin:0 auto;padding:18px 20px 72px;}");
            sb.Append(".site{font:600 12px/1.2 -apple-system,Roboto,sans-serif;letter-spacing:.08em;text-transform:uppercase;color:var(--link);margin:6px 0 10px;}");
            sb.Append($"h1.title{{font-family:-apple-system,Roboto,'Open Sans',sans-serif;font-size:{size + 9}px;line-height:1.22;margin:0 0 12px;font-weight:700;letter-spacing:-.01em;}}");
            sb.Append(".meta{font:13px/1.4 -apple-system,Roboto,sans-serif;color:var(--muted);margin-bottom:14px;}");
            sb.Append(".pill{display:inline-block;font:600 11px/1 -apple-system,Roboto,sans-serif;padding:6px 10px;border-radius:999px;margin-bottom:18px;}");
            sb.Append(".pill.online{background:rgba(90,169,255,.16);color:#5AA9FF}.pill.offline{background:rgba(61,214,140,.16);color:#2fbf78}.pill.cached{background:rgba(245,181,68,.18);color:#d49a2a}");
            sb.Append(".notice{font:14px/1.45 -apple-system,Roboto,sans-serif;background:var(--card);border:1px solid var(--border);border-radius:14px;padding:12px 14px;margin:0 0 18px;color:var(--muted);}");
            sb.Append(".hero{width:100%;border-radius:16px;margin:4px 0 20px;}");
            sb.Append($".content{{text-align:{align};}}");
            sb.Append(".content img{max-width:100%;height:auto;border-radius:12px;display:block;margin:18px auto;}");
            sb.Append(".content figure{margin:18px 0}.content figcaption{font-size:.8em;color:var(--muted);text-align:center;margin-top:-8px}");
            sb.Append(".content a{color:var(--link);text-decoration:none;border-bottom:1px solid rgba(127,127,127,.35)}");
            sb.Append(".content h2,.content h3,.content h4{line-height:1.3;margin:1.6em 0 .5em;font-family:-apple-system,Roboto,sans-serif}");
            sb.Append(".content blockquote{margin:1.2em 0;padding:.2em 0 .2em 1em;border-left:3px solid var(--link);color:var(--muted);font-style:italic}");
            sb.Append(".content pre,.content code{font-family:ui-monospace,Menlo,Consolas,monospace;font-size:.85em;background:var(--card);border-radius:8px}");
            sb.Append(".content pre{padding:12px;overflow-x:auto}.content code{padding:2px 5px}");
            sb.Append(".content table{border-collapse:collapse;width:100%;font-size:.9em;display:block;overflow-x:auto}.content td,.content th{border:1px solid var(--border);padding:6px 8px}");
            sb.Append(".content iframe,.content video{max-width:100%;border:0}");
            sb.Append("mark{background:rgba(255,138,61,.35);color:inherit;border-radius:3px;padding:0 2px}");
            sb.Append(".end{margin-top:36px;padding-top:18px;border-top:1px solid var(--border);font:14px -apple-system,Roboto,sans-serif;color:var(--muted);text-align:center}");
            sb.Append(".end a{color:var(--link);font-weight:600;text-decoration:none}");
            sb.Append("</style></head><body><div class='wrap'>");

            if (!string.IsNullOrWhiteSpace(item.SiteName))
                sb.Append("<div class='site'>").Append(HtmlHelper.Encode(item.SiteName)).Append("</div>");
            sb.Append("<h1 class='title'>").Append(HtmlHelper.Encode(item.Title)).Append("</h1>");
            sb.Append("<div class='meta'>").Append(string.Join(" · ", meta)).Append("</div>");
            sb.Append(status);
            if (!string.IsNullOrWhiteSpace(notice))
                sb.Append("<div class='notice'>").Append(notice).Append("</div>");
            sb.Append(hero);
            sb.Append("<div class='content'>").Append(bodyHtml).Append("</div>");

            if (!string.IsNullOrWhiteSpace(item.Link))
                sb.Append("<div class='end'>Fin de l'article · <a href='rssprout://original'>Lire sur le site d'origine</a></div>");

            sb.Append("</div></body></html>");
            return Task.FromResult(sb.ToString());
        }

        /// <summary>Page simple (chargement, erreur, hors-ligne).</summary>
        public static string Message(string title, string text)
        {
            var (bg, fg, muted, _, _, _) = GetColors();
            return $"<html><head><meta name='viewport' content='width=device-width, initial-scale=1'></head>" +
                   $"<body style='background:{bg};color:{fg};font-family:-apple-system,Roboto,sans-serif;display:flex;align-items:center;justify-content:center;height:90vh;margin:0;text-align:center;padding:24px'>" +
                   $"<div><h3 style='margin:0 0 8px'>{HtmlHelper.Encode(title)}</h3><p style='color:{muted};margin:0'>{HtmlHelper.Encode(text)}</p></div></body></html>";
        }

        private static (string bg, string fg, string muted, string link, string card, string border) GetColors()
        {
            var theme = AppSettings.ReaderTheme;
            if (theme == "app") theme = ThemeService.Palette.IsDark ? "dark" : "light";

            return theme switch
            {
                "light" => ("#FBFAF7", "#1C1D21", "#6B7080", "#D9631F", "#F0EDE6", "#E3DFD7"),
                "sepia" => ("#F4ECD8", "#3B2F23", "#7A6A55", "#A8541E", "#EADFC6", "#D9CBAE"),
                "black" => ("#000000", "#D8DAE0", "#7C8290", "#FF8A3D", "#121317", "#23262D"),
                _ => ("#0E1014", "#E4E6EB", "#8E95A3", "#FF8A3D", "#191C22", "#2B303A")
            };
        }

        private static string RemoveDuplicateTitle(string html, string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return html;
            var m = Regex.Match(html, @"^\s*<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (m.Success && HtmlHelper.NormalizeForSearch(HtmlHelper.StripHtml(m.Groups[1].Value)) == HtmlHelper.NormalizeForSearch(title))
                return html.Substring(m.Length);
            return html;
        }

        /// <summary>Images locales : on garde file:// si le fichier existe, sinon on remet l'URL d'origine.</summary>
        private static string FixLocalImages(string html)
        {
            if (!html.Contains("file://", StringComparison.OrdinalIgnoreCase)) return html;

            var result = LocalImgRegex.Replace(html, m =>
            {
                var path = Uri.UnescapeDataString(m.Groups[2].Value);
                return File.Exists(path) ? m.Value : "src=\"\"";
            });

            // Fichier manquant -> URL d'origine
            result = Regex.Replace(result, @"src=""""\s+data-orig-src=""([^""]+)""|data-orig-src=""([^""]+)""\s+src=""""",
                m => $"src=\"{(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)}\"");
            return result;
        }

        /// <summary>Surligne les mots-clés surveillés (hors balises).</summary>
        private static string Highlight(string html, IReadOnlyList<string> keywords)
        {
            try
            {
                foreach (var k in keywords.Where(k => k.Length >= 3).Take(10))
                {
                    var pattern = $@"(?<=>[^<]*)\b({Regex.Escape(k)})\b";
                    html = Regex.Replace(html, pattern, "<mark>$1</mark>", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(400));
                }
            }
            catch (RegexMatchTimeoutException) { }
            return html;
        }
    }
}
