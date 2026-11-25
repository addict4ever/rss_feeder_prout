using AngleSharp;
using AngleSharp.Dom;
using Rss_feeder_prout.Helpers;
using Rss_feeder_prout.Models;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Rss_feeder_prout.Services
{
    /// <summary>Images des articles et icônes des sites.</summary>
    public partial class RssService
    {
        private const int MaxImagesPerArticle = 40;

        /// <summary>
        /// Met en cache les images d'un contenu HTML et remplace leur src par le fichier local.
        /// L'URL d'origine est conservée dans data-orig-src (repli si le fichier disparaît).
        /// </summary>
        public async Task<string> CacheContentImagesAsync(string html, string baseUrl, ImageMode mode, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(html) || mode == ImageMode.None) return html;

            using var context = BrowsingContext.New(Configuration.Default);
            using var doc = await context.OpenAsync(req => req.Content(html), ct);
            if (doc.Body == null) return html;

            var images = doc.Body.QuerySelectorAll("img").Take(MaxImagesPerArticle).ToList();

            // 1. On relève les URL (lecture du DOM sur un seul thread)
            var jobs = new List<(AngleSharp.Dom.IElement img, string src)>();
            foreach (var img in images)
            {
                var src = img.GetAttribute("data-orig-src") ?? img.GetAttribute("src");
                if (string.IsNullOrWhiteSpace(src) || src.StartsWith("file://")) continue;

                if (!src.StartsWith("http", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
                    && Uri.TryCreate(baseUri, src, out var abs))
                    src = abs.AbsoluteUri;

                jobs.Add((img, src));
            }

            // 2. Téléchargements en parallèle (3 max)
            using var gate = new SemaphoreSlim(3);
            var tasks = jobs.Select(async job =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    return await _images.CacheImageAsync(job.src, mode, baseUrl, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { Debug.WriteLine($"[IMAGE] {job.src}: {ex.Message}"); return null; }
                finally { gate.Release(); }
            }).ToList();

            var locals = await Task.WhenAll(tasks);

            // 3. Remplacement des src (de nouveau sur un seul thread)
            for (int i = 0; i < jobs.Count; i++)
            {
                if (string.IsNullOrEmpty(locals[i])) continue;
                jobs[i].img.SetAttribute("data-orig-src", jobs[i].src);
                jobs[i].img.SetAttribute("src", "file://" + locals[i]);
            }

            return doc.Body.InnerHtml;
        }

        /// <summary>Conservé : met en cache les images d'un document AngleSharp.</summary>
        private async Task<string> CacheHtmlImagesLocallyAsync(IDocument document, string baseUrl)
        {
            if (document?.Body == null) return string.Empty;
            return await CacheContentImagesAsync(document.Body.InnerHtml, baseUrl, AppSettings.ManualImageMode == ImageMode.None ? ImageMode.Optimized : AppSettings.ManualImageMode);
        }

        /// <summary>Conservé : télécharge les images d'un HTML (mode des réglages).</summary>
        private Task<string> ProcessAndCacheImagesAsync(string html, string baseUrl)
            => CacheContentImagesAsync(html, baseUrl, AppSettings.ManualImageMode == ImageMode.None ? ImageMode.Optimized : AppSettings.ManualImageMode);

        /// <summary>Conservé : télécharge une image et retourne le chemin local (ou l'URL d'origine en cas d'échec).</summary>
        private async Task<string> DownloadImageToLocalAsync(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl)) return imageUrl;
            var local = await _images.CacheImageAsync(imageUrl, ImageMode.Optimized);
            return local ?? imageUrl;
        }

        /// <summary>
        /// Trouve la première vraie image d'un HTML (lazy-loading compris).
        /// Le proxy Google d'origine n'est appliqué que si l'option est activée.
        /// </summary>
        private string? ExtractImageUrlFromHtml(string? html, string? articleUrl = null)
        {
            if (string.IsNullOrWhiteSpace(html)) return null;

            try
            {
                // 1. Chercher d'abord les attributs de Lazy Loading (souvent la vraie image HD)
                var lazyMatch = Regex.Match(html, @"(?:data-src|data-lazy-src)\s*=\s*['""]([^'""\s]+)", RegexOptions.IgnoreCase);
                string? src = lazyMatch.Success ? lazyMatch.Groups[1].Value : null;

                // 2. Sinon, prendre le src classique
                if (string.IsNullOrEmpty(src))
                    src = FeedParser.FirstImageInHtml(html);

                if (string.IsNullOrEmpty(src) || src.Contains("pixel.wp.com") || src.EndsWith(".gif")) return null;

                src = System.Net.WebUtility.HtmlDecode(src);

                // 3. Normaliser l'URL (rendre absolue)
                if (!string.IsNullOrEmpty(articleUrl) && !src.StartsWith("http") && Uri.TryCreate(articleUrl, UriKind.Absolute, out var baseUri)
                    && Uri.TryCreate(baseUri, src, out var abs))
                {
                    src = abs.AbsoluteUri;
                }

                // 4. Proxy Google pour bypasser les blocages de "Hotlinking" (optionnel)
                return AppSettings.UseGoogleImageProxy ? ImageCacheService.WrapProxy(src) : src;
            }
            catch { return null; }
        }

        // ----------------------------------------------------------------------
        // ICÔNES
        // ----------------------------------------------------------------------

        /// <summary>Télécharge l'icône d'un site. CORRIGÉ : nom de fichier stable (plus de doublons à chaque synchro).</summary>
        public async Task<string?> DownloadAndSaveIconAsync(string url, string siteName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await _httpClient.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode) return null;

                var type = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (type.StartsWith("text/")) return null;

                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length < 50) return null;

                Directory.CreateDirectory(SQLiteService.IconsFolder);
                string fileName = $"icon_{FormatHelper.SafeFileName(siteName)}_{FormatHelper.StableHash(url).Substring(0, 8)}.png";
                string localPath = Path.Combine(SQLiteService.IconsFolder, fileName);

                await File.WriteAllBytesAsync(localPath, bytes, ct);
                return localPath;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[DOWNLOAD] Téléchargement de l'icône annulé.");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DOWNLOAD ERROR] : {ex.Message}");
                return null;
            }
        }

        /// <summary>S'assure que la source a une icône locale (favicon Google en secours, méthode d'origine).</summary>
        public async Task<bool> EnsureSiteIconAsync(FeedSite site, CancellationToken ct = default)
        {
            if (!string.IsNullOrEmpty(site.LocalIconPath) && File.Exists(site.LocalIconPath)) return false;

            bool changed = false;
            var host = site.Host;
            if (string.IsNullOrWhiteSpace(site.IconUrl) && !string.IsNullOrWhiteSpace(host))
            {
                site.IconUrl = $"https://www.google.com/s2/favicons?sz=128&domain={host}";
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(site.IconUrl))
            {
                var local = await DownloadAndSaveIconAsync(site.IconUrl, site.Name, ct);

                // L'icône du flux est parfois invalide : on retente avec le favicon Google
                if (local == null && !site.IconUrl.Contains("google.com/s2/favicons") && !string.IsNullOrWhiteSpace(host))
                {
                    site.IconUrl = $"https://www.google.com/s2/favicons?sz=128&domain={host}";
                    local = await DownloadAndSaveIconAsync(site.IconUrl, site.Name, ct);
                }

                if (!string.IsNullOrEmpty(local))
                {
                    site.LocalIconPath = local;
                    changed = true;
                }
            }

            if (changed) await _dbService.UpdateSiteAsync(site);
            return changed;
        }
    }
}
