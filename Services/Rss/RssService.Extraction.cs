using AngleSharp;
using AngleSharp.Dom;
using Rss_feeder_prout.Helpers;
using Rss_feeder_prout.Models;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rss_feeder_prout.Services
{
    /// <summary>
    /// Extraction d'article complet + méthodes d'origine anti-blocage / paywall :
    ///  CreateStealthHttpClient, HandleDeepNavigationAsync, AutoAcceptCookies, ApplyStructuralBypass,
    ///  ExtractMainContentAsync, PreParseSanitize, FetchRawContentAsync, FinalSanitizeAndOptimizeAsync,
    ///  EnsureUtf8Encoding, FixRelativeUrls, PreCleanHtml.
    /// Elles sont toutes conservées ; les bugs ont été corrigés (voir commentaires "CORRIGÉ").
    /// </summary>
    public partial class RssService
    {
        // ----------------------------------------------------------------------
        // PIPELINE D'EXTRACTION
        // ----------------------------------------------------------------------

        /// <summary>
        /// Télécharge la page d'un article et en extrait le contenu principal (HTML propre).
        /// Ne sauvegarde rien. Retourne null si l'extraction échoue.
        /// </summary>
        public async Task<string?> ExtractArticleHtmlAsync(string url, FeedSite? site, CancellationToken ct = default)
        {
            // CORRIGÉ : une page piégée / énorme / paywall pouvait faire planter l'app pendant l'extraction.
            // Tout est maintenant isolé : en cas de problème on retourne null (l'article reste lisible en résumé).
            try
            {
                var html = await Task.Run(() => ExtractArticleHtmlCoreAsync(url, site, ct), ct);
                if (html != null && html.Length > MaxExtractedChars)
                    html = HtmlHelper.Truncate(html, MaxExtractedChars, addEllipsis: false);
                return html;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                CrashLog.Write($"Extraction {url}", ex);
                return null;
            }
        }

        private const int MaxRawHtmlChars = 4_000_000;
        private const int MaxExtractedChars = 1_500_000;

        /// <summary>
        /// Exécute du code récursif (SmartReader, sérialisation d'un DOM très imbriqué) sur un thread à grande pile :
        /// un dépassement de pile ne peut pas être intercepté et fermait l'application.
        /// </summary>
        private static Task<T> RunWithLargeStack<T>(Func<T> work)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try { tcs.SetResult(work()); }
                catch (Exception ex) { tcs.SetException(ex); }
            }, 64 * 1024 * 1024)
            {
                IsBackground = true,
                Name = "RssProut-Extraction"
            };
            thread.Start();
            return tcs.Task;
        }

        private async Task<string?> ExtractArticleHtmlCoreAsync(string url, FeedSite? site, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            string cleanedUrl = url.Trim().Replace(" ", "%20");
            if (!cleanedUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;

            // Méthodes paywall désactivées (réglage) : client HTTP standard, sans usurpation d'identité
            bool paywall = AppSettings.UsePaywallMethods;
            using var client = paywall ? CreateStealthHttpClient() : CreateStandardHttpClient();

            // 1. Téléchargement du HTML brut (2 tentatives)
            string htmlContent = string.Empty;
            string finalUrl = cleanedUrl;
            for (int attempt = 1; attempt <= 2 && string.IsNullOrEmpty(htmlContent); attempt++)
            {
                try
                {
                    using var response = await client.GetAsync(cleanedUrl, ct);
                    if (response.IsSuccessStatusCode)
                    {
                        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                        htmlContent = EnsureUtf8Encoding(bytes, response.Content.Headers.ContentType?.CharSet);
                        finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? cleanedUrl;
                    }
                    else if (attempt == 1 && ((int)response.StatusCode == 403 || (int)response.StatusCode == 429 || (int)response.StatusCode == 503))
                    {
                        await Task.Delay(900, ct);
                    }
                    else
                    {
                        break;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[EXTRACT] Tentative {attempt} {cleanedUrl}: {ex.Message}");
                    if (attempt == 1) await Task.Delay(600, ct);
                }
            }

            // Secours : client principal (gère aussi les réponses JSON)
            if (string.IsNullOrEmpty(htmlContent))
                htmlContent = await FetchRawContentAsync(_httpClient, cleanedUrl);

            if (string.IsNullOrEmpty(htmlContent)) return null;
            if (htmlContent.Length > MaxRawHtmlChars) htmlContent = htmlContent.Substring(0, MaxRawHtmlChars);

            // Réponse JSON déjà convertie en HTML par FetchRawContentAsync
            if (!htmlContent.Contains("<html", StringComparison.OrdinalIgnoreCase) && !htmlContent.Contains("<body", StringComparison.OrdinalIgnoreCase)
                && HtmlHelper.TextLength(htmlContent) > 200 && htmlContent.TrimStart().StartsWith("<p"))
                return await FinalSanitizeAndOptimizeAsync(htmlContent, finalUrl);

            // 2. Pré-nettoyage (Regex)
            string rawForBypass = htmlContent;
            htmlContent = PreParseSanitize(htmlContent);

            // 3. Parsing AngleSharp
            var config = Configuration.Default;
            using var context = BrowsingContext.New(config);
            using var document = await context.OpenAsync(req => req.Content(htmlContent).Address(finalUrl), ct);

            if (paywall) AutoAcceptCookies(document);

            // 4. Navigation profonde et réparation des URLs
            var deepDocument = paywall ? await HandleDeepNavigationAsync(client, document, finalUrl) : document;
            var activeDocument = (deepDocument != null && deepDocument != document) ? deepDocument : document;

            try
            {
                FixRelativeUrls(activeDocument, finalUrl);

                // 5. Extraction du contenu principal
                string mainContentHtml = string.Empty;

                // CORRIGÉ : ContentSelector = zone de contenu ; CustomCleanSelectors = éléments à retirer
                if (site != null && !string.IsNullOrWhiteSpace(site.ContentSelector))
                {
                    try { mainContentHtml = activeDocument.QuerySelector(site.ContentSelector)?.InnerHtml ?? string.Empty; }
                    catch (Exception ex) { Debug.WriteLine($"[SELECTOR] {site.ContentSelector}: {ex.Message}"); }
                }

                if (string.IsNullOrWhiteSpace(mainContentHtml))
                {
                    mainContentHtml = await ExtractMainContentAsync(activeDocument, finalUrl, activeDocument.Source?.Text ?? htmlContent);
                }

                // 6. Contenu trop court (teaser / contenu masqué par CSS) -> contournement structurel d'origine
                if (paywall && HtmlHelper.TextLength(mainContentHtml) < 900)
                {
                    try
                    {
                        using var bypassDoc = await context.OpenAsync(req => req.Content(rawForBypass).Address(finalUrl), ct);
                        var bypass = await RunWithLargeStack(() => ApplyStructuralBypass(bypassDoc, site!));
                        if (HtmlHelper.TextLength(bypass) > HtmlHelper.TextLength(mainContentHtml) * 1.2)
                            mainContentHtml = bypass;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[BYPASS] {ex.Message}");
                    }
                }

                // 7. Éléments à retirer (sélecteurs du site)
                if (site != null && !string.IsNullOrWhiteSpace(site.CustomCleanSelectors))
                    mainContentHtml = await RemoveSelectorsAsync(mainContentHtml, site.CustomCleanSelectors);

                // 8. Nettoyage final et optimisation
                string finalCleanHtml = await FinalSanitizeAndOptimizeAsync(mainContentHtml, finalUrl);

                return HtmlHelper.TextLength(finalCleanHtml) >= 120 ? finalCleanHtml : null;
            }
            finally
            {
                if (deepDocument != null && deepDocument != document) deepDocument.Dispose();
            }
        }

        /// <summary>
        /// Télécharge, extrait et ENREGISTRE le contenu complet d'un article (images selon le mode choisi).
        /// </summary>
        public async Task<bool> DownloadAndSaveItemContentAsync(RssItem item, ImageMode imageMode, CancellationToken ct = default)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Link)) return false;

            // Déjà complet (et images faites si demandées) -> rien à faire
            if (item.IsDownloaded && (imageMode == ImageMode.None || item.ImagesCached)) return true;

            await _semaphore.WaitAsync(ct);
            try
            {
                item.IsBusy = true;

                string? html = null;

                // Contenu déjà présent (ex : flux complet) : on ne fait que les images
                if (item.IsDownloaded)
                {
                    if (string.IsNullOrEmpty(item.ContentHtml) && item.Id > 0)
                    {
                        var full = await _dbService.GetItemByIdAsync(item.Id);
                        html = full?.ContentHtml;
                    }
                    else
                    {
                        html = item.ContentHtml;
                    }
                }

                if (string.IsNullOrWhiteSpace(html))
                {
                    var site = item.SiteId.HasValue ? await _dbService.GetSiteByIdAsync(item.SiteId.Value) : null;
                    html = await ExtractArticleHtmlAsync(item.Link, site, ct);
                }

                if (string.IsNullOrWhiteSpace(html)) return false;

                await SaveExtractedContentAsync(item, html, imageMode, ct);
                Debug.WriteLine($"[OFFLINE READY] Texte{(imageMode != ImageMode.None ? " et images" : string.Empty)} stockés pour : {item.Title}");
                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CRITICAL ERROR] {item.Link} : {ex.Message}");
                return false;
            }
            finally
            {
                item.IsBusy = false;
                _semaphore.Release();
            }
        }

        /// <summary>Conservé : signature d'origine (utilise le mode d'images des réglages).</summary>
        public Task<bool> DownloadAndSaveItemContentAsync(RssItem item, CancellationToken ct = default)
            => DownloadAndSaveItemContentAsync(item, AppSettings.ManualImageMode, ct);

        /// <summary>Met les images en cache selon le mode puis enregistre le contenu.</summary>
        public async Task SaveExtractedContentAsync(RssItem item, string html, ImageMode imageMode, CancellationToken ct = default)
        {
            string finalHtml = html;
            bool imagesDone = false;
            string? leadLocal = null;

            if (imageMode != ImageMode.None)
            {
                finalHtml = await CacheContentImagesAsync(html, item.Link, imageMode, ct);
                imagesDone = true;

                if (string.IsNullOrEmpty(item.LocalImagePath) && !string.IsNullOrEmpty(item.ImageUrl))
                    leadLocal = await _images.CacheImageAsync(item.ImageUrl, ImageMode.Optimized, item.Link, ct);
            }

            await _dbService.SaveArticleContentAsync(item, finalHtml, imagesDone, leadLocal);
            ItemContentDownloaded?.Invoke(this, item);
        }

        private static async Task<string> RemoveSelectorsAsync(string html, string selectors)
        {
            if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(selectors)) return html;
            using var context = BrowsingContext.New(Configuration.Default);
            using var doc = await context.OpenAsync(req => req.Content(html));
            foreach (var raw in selectors.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var selector = raw.Trim();
                if (selector.Length == 0) continue;
                try
                {
                    foreach (var el in doc.QuerySelectorAll(selector).ToList()) el.Remove();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CLEAN SELECTOR] '{selector}' invalide : {ex.Message}");
                }
            }
            return doc.Body?.InnerHtml ?? html;
        }

        // ----------------------------------------------------------------------
        // MÉTHODES D'ORIGINE
        // ----------------------------------------------------------------------

        /// <summary>CORRIGÉ : gère tous les encodages (windows-1252, iso-8859-x...) + balise meta charset.</summary>
        private string EnsureUtf8Encoding(byte[] bytes, string? charset)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(charset))
                {
                    // Cherche <meta charset="..."> dans les premiers Ko
                    var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 4096));
                    var m = Regex.Match(head, @"<meta[^>]+charset\s*=\s*[""']?([A-Za-z0-9_\-]+)", RegexOptions.IgnoreCase);
                    if (m.Success) charset = m.Groups[1].Value;
                }

                if (!string.IsNullOrWhiteSpace(charset) && !charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase)
                    && !charset.Equals("utf8", StringComparison.OrdinalIgnoreCase))
                {
                    // Utilise l'encodage déclaré (vieux sites : Europe de l'Ouest)
                    return Encoding.GetEncoding(charset.Trim('"')).GetString(bytes);
                }
            }
            catch { }
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>CORRIGÉ : ignore mailto:, javascript:, data: et les URL invalides (plus d'exception).</summary>
        private void FixRelativeUrls(IDocument document, string baseUrl)
        {
            if (document == null || string.IsNullOrEmpty(baseUrl)) return;
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)) return;

            // Réparer les images (src="/img.jpg" -> src="http://site.com/img.jpg")
            foreach (var img in document.QuerySelectorAll("img"))
            {
                foreach (var attr in new[] { "src", "data-src", "data-lazy-src", "data-original" })
                {
                    var src = img.GetAttribute(attr);
                    if (string.IsNullOrEmpty(src) || src.StartsWith("http") || src.StartsWith("data:")) continue;
                    if (src.StartsWith("//")) { img.SetAttribute(attr, baseUri.Scheme + ":" + src); continue; }
                    if (Uri.TryCreate(baseUri, src, out var abs)) img.SetAttribute(attr, abs.AbsoluteUri);
                }
            }

            // Réparer les liens
            foreach (var a in document.QuerySelectorAll("a[href]"))
            {
                var href = a.GetAttribute("href");
                if (string.IsNullOrEmpty(href) || href.StartsWith("http") || href.StartsWith("#")
                    || href.StartsWith("mailto:") || href.StartsWith("javascript:") || href.StartsWith("tel:")) continue;
                if (Uri.TryCreate(baseUri, href, out var abs)) a.SetAttribute("href", abs.AbsoluteUri);
            }
        }

        private string PreParseSanitize(string html)
        {
            if (string.IsNullOrEmpty(html)) return html;

            // Retire les balises lourdes qui font ramer le parsing
            html = Regex.Replace(html, @"<script\b[^>]*>([\s\S]*?)</script>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<style\b[^>]*>([\s\S]*?)</style>", "", RegexOptions.IgnoreCase);

            // Coupe le HTML inutile après l'article (très efficace pour VDM et blogs)
            // CORRIGÉ : seulement s'il n'y a qu'un seul <article> (sinon on perdait le contenu sur les pages à plusieurs blocs)
            if (Regex.Matches(html, @"</article>", RegexOptions.IgnoreCase).Count == 1)
            {
                html = Regex.Replace(html, @"</article>.*", "</article></body></html>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            }

            return html;
        }

        private async Task<string> FetchRawContentAsync(HttpClient client, string link)
        {
            try
            {
                // 1. GESTION DU TIMEOUT ET TENTATIVE DE RÉCUPÉRATION
                using var response = await client.GetAsync(link, HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[AVERTISSEMENT] Code {response.StatusCode} pour {link}");
                    return string.Empty;
                }

                // 2. DÉTECTION DU TYPE DE CONTENU RÉEL
                var contentType = response.Content.Headers.ContentType?.MediaType?.ToLower() ?? "";

                // 3. TRAITEMENT SI C'EST UN FLUX JSON (JSON Feed Standard)
                if (contentType.Contains("json") || link.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    var jsonData = await response.Content.ReadAsStringAsync();
                    using var jsonDoc = JsonDocument.Parse(jsonData);
                    var root = jsonDoc.RootElement;

                    if (root.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
                    {
                        var firstItem = items[0];
                        if (firstItem.TryGetProperty("content_html", out var html)) return html.GetString() ?? string.Empty;
                        if (firstItem.TryGetProperty("content_text", out var text)) return $"<p>{text.GetString()}</p>";
                    }

                    if (root.TryGetProperty("content", out var directContent)) return directContent.GetString() ?? string.Empty;

                    return string.Empty;
                }

                // 4. TRAITEMENT HTML AVEC AUTO-DÉTECTION DE L'ENCODAGE
                var bytes = await response.Content.ReadAsByteArrayAsync();
                var charset = response.Content.Headers.ContentType?.CharSet;
                string htmlContent = EnsureUtf8Encoding(bytes, charset);

                // 5. VÉRIFICATION DE SÉCURITÉ (Anti-Bot Check)
                if (htmlContent.Contains("Cloudflare") && (htmlContent.Contains("votre navigateur") || htmlContent.Contains("checking your browser")))
                {
                    Debug.WriteLine("[ALERTE] Détecté par Cloudflare sur : " + link);
                }

                return htmlContent;
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"[ERREUR RÉSEAU] Impossible de joindre {link} : {ex.Message}");
                return string.Empty;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERREUR CRITIQUE] FetchRawContent : {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Navigation profonde ("Lire la suite").
        /// CORRIGÉ : l'ancienne version acceptait presque n'importe quel élément ("plus", "post", "nav"...)
        /// et pouvait remplacer l'article par une autre page, avec 2 à 4,5 s d'attente PAR article.
        /// Maintenant : seulement de vrais liens "lire la suite" du même site, et seulement si la page ressemble à un extrait.
        /// </summary>
        private async Task<IDocument> HandleDeepNavigationAsync(HttpClient client, IDocument document, string originalLink)
        {
            var rnd = new Random();

            try
            {
                // Si la page contient déjà un vrai article, on ne bouge pas
                int paragraphText = document.QuerySelectorAll("article p, main p, [class*='content'] p, [class*='entry'] p")
                                            .Sum(p => p.TextContent?.Length ?? 0);
                if (paragraphText > 2500) return document;

                // 1. RECHERCHE (Sélecteurs de déclenchement sémantiques)
                var keywords = new[]
                {
                    // FR
                    "lire la suite", "lire l'article", "lire l’article", "article complet", "continuer la lecture",
                    "voir l'article", "voir l’article", "suite de l'article", "suite de l’article",
                    // EN
                    "read more", "continue reading", "full article", "see full article", "full story", "read the full", "read full"
                };

                if (!Uri.TryCreate(originalLink, UriKind.Absolute, out var originalUri)) return document;

                var candidates = document.QuerySelectorAll("a[href]")
                    .Where(el =>
                    {
                        var text = (el.TextContent ?? "").Trim().ToLowerInvariant();
                        var className = (el.ClassName ?? "").ToLowerInvariant();
                        var aria = el.GetAttribute("aria-label")?.ToLowerInvariant() ?? "";

                        bool matchKeywords = (text.Length <= 60 && keywords.Any(k => text.Contains(k)))
                                             || keywords.Any(k => aria.Contains(k))
                                             || className.Contains("read-more") || className.Contains("readmore") || className.Contains("more-link");

                        // éviter faux positifs (liens sociaux, ads)
                        bool blacklist = className.Contains("facebook") || className.Contains("twitter") || className.Contains("instagram")
                                         || className.Contains("promo") || className.Contains("share");

                        return matchKeywords && !blacklist;
                    })
                    .ToList();

                foreach (var trigger in candidates)
                {
                    string? nextUrl = trigger.GetAttribute("href") ?? trigger.GetAttribute("data-url");
                    if (string.IsNullOrEmpty(nextUrl) || nextUrl.StartsWith("#")) continue;
                    if (!Uri.TryCreate(originalUri, nextUrl, out var fullUri)) continue;

                    // Même site uniquement, et pas la même page
                    if (!fullUri.Host.EndsWith(originalUri.Host.Replace("www.", ""), StringComparison.OrdinalIgnoreCase)) continue;
                    if (fullUri.AbsolutePath.TrimEnd('/') == originalUri.AbsolutePath.TrimEnd('/')) continue;

                    // --- SIMULATION DE COMPORTEMENT HUMAIN (délai réduit) ---
                    await Task.Delay(rnd.Next(300, 900));

                    try
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, fullUri);
                        request.Headers.Referrer = originalUri;
                        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                        using var response = await client.SendAsync(request);
                        if (!response.IsSuccessStatusCode) continue;

                        var bytes = await response.Content.ReadAsByteArrayAsync();
                        string newHtml = EnsureUtf8Encoding(bytes, response.Content.Headers.ContentType?.CharSet);

                        // Vérification si le nouveau contenu est réellement plus long (éviter les redirections inutiles)
                        if (newHtml.Length < (document.Source?.Text?.Length ?? 0) * 0.8)
                            continue;

                        var context = BrowsingContext.New(Configuration.Default);
                        var nextDocument = await context.OpenAsync(req => req.Content(PreParseSanitize(newHtml)).Address(fullUri.ToString()));

                        Debug.WriteLine($"[DEEP NAV] Passage au contenu complet : {fullUri}");
                        return nextDocument;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[DEBUG] Échec de la navigation profonde : {ex.Message}");
                    }
                    break; // une seule tentative
                }

                // 3. AUTO-DETECTION DES "IFRAMES" DE CONTENU (Certains paywalls utilisent des cadres)
                var iframeContent = document.QuerySelector("iframe.article-content, iframe[src*='news']");
                if (iframeContent != null)
                {
                    string? iframeSrc = iframeContent.GetAttribute("src");
                    if (!string.IsNullOrEmpty(iframeSrc) && Uri.TryCreate(originalUri, iframeSrc, out var iframeUri))
                    {
                        string iframeHtml = await client.GetStringAsync(iframeUri);
                        return await BrowsingContext.New(Configuration.Default).OpenAsync(req => req.Content(iframeHtml).Address(iframeUri.ToString()));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DEEP NAV] {ex.Message}");
            }

            return document;
        }

        /// <summary>Client HTTP normal (identité honnête de l'application), utilisé quand les méthodes paywall sont désactivées.</summary>
        private static HttpClient CreateStandardHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                AllowAutoRedirect = true,
                UseCookies = true,
                CookieContainer = new CookieContainer()
            };
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Mobile Safari/537.36 RssProut/2.0");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "fr-FR,fr;q=0.9,en;q=0.8");
            return client;
        }

        private HttpClient CreateStealthHttpClient()
        {
            var rnd = new Random();

            // 1. Dictionnaires de navigation pour cohérence (UA + Client Hints)
            // Chaque entrée est un profil cohérent pour ne pas envoyer un UA de Chrome avec des indices de Firefox
            var browserProfiles = new[]
            {
                new {
                    UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
                    CH_UA = "\"Google Chrome\";v=\"124\", \"Not:A-Brand\";v=\"8\", \"Chromium\";v=\"124\"",
                    Platform = "\"Windows\"",
                    Mobile = "?0"
                },
                new {
                    UA = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
                    CH_UA = "\"Google Chrome\";v=\"124\", \"Not:A-Brand\";v=\"8\", \"Chromium\";v=\"124\"",
                    Platform = "\"macOS\"",
                    Mobile = "?0"
                },
                new {
                    UA = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
                    CH_UA = "\"Google Chrome\";v=\"124\", \"Not:A-Brand\";v=\"8\", \"Chromium\";v=\"124\"",
                    Platform = "\"Linux\"",
                    Mobile = "?0"
                }
            };

            var profile = browserProfiles[rnd.Next(browserProfiles.Length)];
            string[] referrers = { "https://www.google.com/", "https://www.bing.com/", "https://news.google.com/", "https://duckduckgo.com/" };

            var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                AutomaticDecompression = DecompressionMethods.All,
                AllowAutoRedirect = true,
                UseCookies = true,
                // On évite d'utiliser un proxy système pour garder une IP propre si possible
                Proxy = null,
                UseProxy = false
            };

            var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(30);

            // --- CONFIGURATION DES HEADERS (SIMULATION NIVEAU NAVIGATEUR) ---

            // A. Identité de base
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", profile.UA);
            client.DefaultRequestHeaders.Referrer = new Uri(referrers[rnd.Next(referrers.Length)]);

            // B. Client Hints (Crucial pour bypasser les WAF modernes comme Cloudflare/Akamai)
            client.DefaultRequestHeaders.TryAddWithoutValidation("sec-ch-ua", profile.CH_UA);
            client.DefaultRequestHeaders.TryAddWithoutValidation("sec-ch-ua-mobile", profile.Mobile);
            client.DefaultRequestHeaders.TryAddWithoutValidation("sec-ch-ua-platform", profile.Platform);

            // C. Capacités du navigateur (Acceptation de formats compressés et modernes)
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "fr-FR,fr;q=0.9,en-US;q=0.8,en;q=0.7");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");

            // D. Headers de Navigation (Simulation d'une requête utilisateur réelle)
            client.DefaultRequestHeaders.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-User", "?1");
            client.DefaultRequestHeaders.TryAddWithoutValidation("DNT", "1"); // Do Not Track

            // E. Gestion du Cache (Pour ne pas avoir l'air d'un bot qui rafraîchit en boucle)
            client.DefaultRequestHeaders.TryAddWithoutValidation("Cache-Control", "max-age=0");

            // F. Obfuscation d'IP (X-Forwarded-For avec IP résidentielle aléatoire factice)
            string fakeIp = $"{rnd.Next(1, 255)}.{rnd.Next(1, 255)}.{rnd.Next(1, 255)}.{rnd.Next(1, 255)}";
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Forwarded-For", fakeIp);

            return client;
        }

        /// <summary>
        /// CORRIGÉ : ":contains()" n'est pas du CSS valide (AngleSharp levait une exception) -> recherche par texte.
        /// Maintenant réellement appelée dans le pipeline.
        /// </summary>
        private void AutoAcceptCookies(IDocument document)
        {
            if (document?.Body == null) return;

            var consentSelectors = new[]
            {
                "button[aria-label*='Accepter']",
                "#onetrust-accept-btn-handler", // OneTrust (très commun)
                "#onetrust-banner-sdk",
                "#didomi-host",                 // Didomi (Radio-Canada, presse FR)
                ".didomi-popup-container",
                "#cmpbox", "#cmpwrapper",
                ".qc-cmp2-container",           // Quantcast
                ".consent-accept",
                "button[id*='accept']",
                "[class*='cookie-banner']", "[id*='cookie-banner']", "[class*='cookie-consent']"
            };

            foreach (var selector in consentSelectors)
            {
                try
                {
                    foreach (var el in document.QuerySelectorAll(selector).ToList())
                    {
                        Debug.WriteLine($"[CONSENT] Bannière détectée : retrait de {selector}");
                        el.Remove();
                    }
                }
                catch { }
            }

            // Boutons "Accepter" / "Tout accepter" (anciens :contains)
            foreach (var btn in document.QuerySelectorAll("button, a").ToList())
            {
                var t = (btn.TextContent ?? string.Empty).Trim().ToLowerInvariant();
                if (t == "accepter" || t == "tout accepter" || t == "j'accepte" || t == "accept all" || t == "accept")
                    btn.Remove();
            }

            // Si c'est une bannière, on force aussi le retrait du overlay et on rétablit le scroll
            try
            {
                foreach (var overlay in document.QuerySelectorAll(".modal-backdrop, .overlay, #blocking-layer").ToList()) overlay.Remove();
            }
            catch { }
            document.Body.SetAttribute("style", "overflow: auto !important;");
        }

        /// <summary>
        /// Contournement structurel (contenus masqués par CSS / flou / overlay) — méthode d'origine.
        /// CORRIGÉ : SmartReader recevait le HTML comme si c'était une URL (il n'a donc jamais fonctionné ici).
        /// </summary>
        private string ApplyStructuralBypass(IDocument document, FeedSite site)
        {
            if (document == null) return string.Empty;

            AutoAcceptCookies(document);

            // 1. SUPPRESSION DES TAGS INUTILES (Rapide et léger)
            var nuisanceTags = new[] {
                "script", "noscript", "style", "iframe", "nav", "footer", "header",
                "aside", "form", "button", "svg", "canvas", "meta", "link",
                "embed", "object"
            };
            foreach (var tag in nuisanceTags)
            {
                foreach (var el in document.QuerySelectorAll(tag).ToList()) el.Remove();
            }

            // 2. NEUTRALISATION DES TRAPS & ÉLÉMENTS INVISIBLES (Optimisé)
            var traps = document.QuerySelectorAll("[style*='display:none'], [style*='display: none'], [aria-hidden='true']").ToList();
            foreach (var t in traps) { try { t.Remove(); } catch { } }

            // 3. ANTI-BLUR ET ANTI-GRAYSCALE
            var hiddenByCss = document.QuerySelectorAll("[style*='filter'], [style*='opacity'], [style*='webkit-filter']").ToList();
            foreach (var el in hiddenByCss)
            {
                try
                {
                    el.SetAttribute("style", "filter: none !important; -webkit-filter: none !important; opacity: 1 !important; visibility: visible !important; display: block !important;");
                }
                catch { }
            }

            // 4. RESTAURATION DU SCROLL (Ciblage spécifique)
            foreach (var node in document.QuerySelectorAll("html, body"))
            {
                try
                {
                    node.SetAttribute("style", "overflow: visible !important; position: static !important; height: auto !important; display: block !important;");
                }
                catch { }
            }

            // 5. On garde les plus gros blocs de texte
            var allBlocks = document.QuerySelectorAll("div, section, article, main, p");
            var bestCandidates = allBlocks
                .Where(e =>
                {
                    int textLength = e.TextContent?.Trim().Length ?? 0;
                    return textLength > 200 && e.QuerySelectorAll("p, h1, h2, h3").Any();
                })
                .OrderByDescending(e => e.TextContent?.Length ?? 0)
                .Take(3)
                .ToList();

            if (bestCandidates.Any() && document.Body != null)
            {
                var wrapper = document.CreateElement("div");
                wrapper.SetAttribute("id", "rss-prout-extracted-content");

                foreach (var candidate in bestCandidates)
                {
                    // On évite les doublons (bloc déjà contenu dans un autre bloc sélectionné)
                    if (!bestCandidates.Any(other => other != candidate && other.Contains(candidate)))
                    {
                        wrapper.AppendChild(candidate);
                    }
                }

                document.Body.InnerHtml = string.Empty;
                document.Body.AppendChild(wrapper);
            }

            // 6. SMARTREADER (Le travail final d'extraction)
            try
            {
                var url = !string.IsNullOrEmpty(document.Url) && document.Url.StartsWith("http") ? document.Url
                          : (!string.IsNullOrEmpty(site?.SiteUrl) ? site!.SiteUrl : "https://localhost/");
                var reader = new SmartReader.Reader(url, document.DocumentElement.OuterHtml);
                var article = reader.GetArticle();

                if (article != null && article.IsReadable)
                {
                    return article.Content;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SMARTREADER ERROR] : {ex.Message}");
            }

            // Fallback : Retourne le body nettoyé si SmartReader échoue
            return document.Body?.InnerHtml ?? string.Empty;
        }

        private async Task<string> ExtractMainContentAsync(IDocument document, string url, string rawHtml)
        {
            // 1. TENTATIVE SMARTREADER (Standard de l'industrie)
            SmartReader.Article? article = null;
            try
            {
                var reader = new SmartReader.Reader(url, rawHtml);
                article = await RunWithLargeStack(() => reader.GetArticle());

                // Si SmartReader réussit avec un bon volume de texte, on valide
                if (article != null && article.IsReadable && article.Content.Length > 1000)
                    return article.Content;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SMARTREADER] {ex.Message}");
            }

            // 2. ANALYSE DE DENSITÉ TEXTUELLE (Algorithme maison)
            var candidates = document.QuerySelectorAll("div, section, article, main")
                .Select(e =>
                {
                    int textLength = e.TextContent?.Trim().Length ?? 0;
                    int htmlLength = Math.Max(1, e.InnerHtml?.Length ?? 1);
                    double density = (double)textLength / htmlLength;

                    int pCount = e.QuerySelectorAll("p").Length;

                    double finalScore = textLength * density;
                    if (pCount >= 2) finalScore *= 1.5;
                    if (e.TagName.ToLower() == "article") finalScore *= 1.2;

                    // Malus pour les zones de liens (menus, footers)
                    int linkLength = e.QuerySelectorAll("a").Sum(a => a.TextContent?.Length ?? 0);
                    if (linkLength > textLength * 0.5) finalScore *= 0.3;

                    return new { Element = e, Score = finalScore };
                })
                .OrderByDescending(c => c.Score)
                .ToList();

            var bestCandidate = candidates.FirstOrDefault();
            string extractedHtml = string.Empty;

            if (bestCandidate != null && bestCandidate.Score > 100)
            {
                extractedHtml = bestCandidate.Element.InnerHtml;
                Debug.WriteLine($"[DEBUG] Extraction par densité réussie (Score: {bestCandidate.Score:F0})");
            }
            else
            {
                // 3. FALLBACK DE DERNIER RECOURS (Sélecteurs de secours connus)
                var fallbackSelectors = new[] { ".post-content", ".article-body", "#entry-content", ".entry-content-wrapper", ".entry-content", "article" };
                foreach (var selector in fallbackSelectors)
                {
                    var el = document.QuerySelector(selector);
                    if (el != null && (el.TextContent?.Length ?? 0) > 200)
                    {
                        extractedHtml = el.InnerHtml;
                        break;
                    }
                }
            }

            // SmartReader a trouvé quelque chose de plus long que l'heuristique ?
            if (article != null && article.IsReadable && HtmlHelper.TextLength(article.Content) > HtmlHelper.TextLength(extractedHtml))
                extractedHtml = article.Content;

            if (string.IsNullOrEmpty(extractedHtml)) extractedHtml = document.Body?.InnerHtml ?? string.Empty;

            // 4. IMAGE DE SECOURS (OpenGraph) si aucune image dans le contenu
            if (!extractedHtml.Contains("<img", StringComparison.OrdinalIgnoreCase))
            {
                var featuredImg = article?.FeaturedImage;
                if (string.IsNullOrEmpty(featuredImg))
                {
                    featuredImg = document.QuerySelector("meta[property='og:image']")?.GetAttribute("content")
                                ?? document.QuerySelector("link[rel='image_src']")?.GetAttribute("href");
                }
                if (!string.IsNullOrEmpty(featuredImg))
                    extractedHtml = $"<img src='{WebUtility.HtmlEncode(featuredImg)}' />" + extractedHtml;
            }

            // (Le titre n'est plus injecté : le lecteur l'affiche déjà en en-tête.)
            return extractedHtml;
        }

        /// <summary>
        /// Nettoyage final : liste blanche de balises, images "lazy", suppression des pixels espions.
        /// CORRIGÉ : le proxy Google cassait le mode hors-ligne (appliqué APRÈS la mise en cache) ;
        /// il n'est plus appliqué ici (secours optionnel dans ImageCacheService).
        /// </summary>
        private async Task<string> FinalSanitizeAndOptimizeAsync(string html, string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;

            var config = Configuration.Default;
            using var context = BrowsingContext.New(config);
            using var doc = await context.OpenAsync(req => req.Content(html));
            if (doc.Body == null) return string.Empty;

            var authorizedTags = new HashSet<string> {
                "P", "H1", "H2", "H3", "H4", "H5", "H6", "BR", "B", "I", "U", "S", "STRONG", "EM", "MARK", "SMALL", "SUB", "SUP",
                "UL", "OL", "LI", "DL", "DT", "DD", "IMG", "A", "BLOCKQUOTE", "Q", "CITE", "FIGURE", "FIGCAPTION", "HR",
                "PRE", "CODE", "TABLE", "THEAD", "TBODY", "TFOOT", "TR", "TD", "TH", "CAPTION"
            };
            var unwrapTags = new HashSet<string> {
                "DIV", "SECTION", "SPAN", "ARTICLE", "MAIN", "PICTURE", "FONT", "CENTER", "TIME", "ABBR", "LABEL", "HEADER", "SUMMARY", "DETAILS"
            };

            // Images "lazy" et pixels espions AVANT de retirer les attributs
            foreach (var img in doc.Body.QuerySelectorAll("img").ToList())
            {
                var realSrc = img.GetAttribute("data-src") ?? img.GetAttribute("data-lazy-src") ?? img.GetAttribute("data-original")
                              ?? FirstFromSrcset(img.GetAttribute("srcset") ?? img.GetAttribute("data-srcset")) ?? img.GetAttribute("src");

                var w = img.GetAttribute("width");
                var h = img.GetAttribute("height");
                bool tiny = (w == "1" || w == "0") || (h == "1" || h == "0");
                if (string.IsNullOrWhiteSpace(realSrc) || realSrc.StartsWith("data:") || tiny ||
                    realSrc.Contains("pixel.wp.com") || realSrc.Contains("feeds.feedburner.com/~r") || realSrc.Contains("/tracking/"))
                {
                    img.Remove();
                    continue;
                }

                if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) && Uri.TryCreate(baseUri, realSrc, out var abs))
                    realSrc = abs.AbsoluteUri;
                img.SetAttribute("src", ImageCacheService.UnwrapProxy(realSrc));
            }

            // <source> de <picture> : récupère une image si le <picture> n'en a pas
            foreach (var picture in doc.Body.QuerySelectorAll("picture").ToList())
            {
                if (picture.QuerySelector("img") == null)
                {
                    var src = FirstFromSrcset(picture.QuerySelector("source")?.GetAttribute("srcset"));
                    if (!string.IsNullOrEmpty(src))
                    {
                        var img = doc.CreateElement("img");
                        img.SetAttribute("src", src);
                        picture.AppendChild(img);
                    }
                }
            }

            var allElements = doc.Body.QuerySelectorAll("*").ToList();
            foreach (var el in allElements)
            {
                if (!authorizedTags.Contains(el.TagName))
                {
                    if (unwrapTags.Contains(el.TagName))
                    {
                        // UNWRAP : on déplace tous les enfants juste avant l'élément puis on le supprime
                        while (el.FirstChild != null)
                        {
                            el.Before(el.FirstChild);
                        }
                        el.Remove();
                    }
                    else
                    {
                        el.Remove(); // script, style, iframe, svg, source, etc.
                    }
                    continue;
                }

                // Nettoyage des attributs (on ne garde que l'essentiel)
                var attrNames = el.Attributes.Select(a => a.Name).ToList();
                foreach (var attr in attrNames)
                {
                    if (attr != "src" && attr != "href" && attr != "title" && attr != "alt" && attr != "colspan" && attr != "rowspan" && attr != "data-orig-src")
                    {
                        el.RemoveAttribute(attr);
                    }
                }
            }

            foreach (var link in doc.Body.QuerySelectorAll("a").ToList())
            {
                var href = link.GetAttribute("href");
                if (string.IsNullOrEmpty(href) || href.StartsWith("javascript:"))
                {
                    // Lien vide : on garde le texte
                    while (link.FirstChild != null) link.Before(link.FirstChild);
                    link.Remove();
                    continue;
                }
                link.SetAttribute("target", "_blank");
            }

            // Paragraphes vides
            foreach (var p in doc.Body.QuerySelectorAll("p, li, h2, h3, h4").ToList())
            {
                if (string.IsNullOrWhiteSpace(p.TextContent) && p.QuerySelector("img") == null && p.QuerySelector("br") == null)
                    p.Remove();
            }

            var result = doc.Body.InnerHtml;
            if (!result.Contains("<pre", StringComparison.OrdinalIgnoreCase))
                result = Regex.Replace(result, @"\s+", " ");
            return result.Trim();
        }

        private static string? FirstFromSrcset(string? srcset)
        {
            if (string.IsNullOrWhiteSpace(srcset)) return null;
            // Prend la plus grande définition (dernier élément)
            var parts = srcset.Split(',', StringSplitOptions.RemoveEmptyEntries)
                              .Select(p => p.Trim().Split(' ')[0])
                              .Where(p => !string.IsNullOrWhiteSpace(p) && !p.StartsWith("data:"))
                              .ToList();
            return parts.LastOrDefault();
        }

        // Méthode de pré-nettoyage pour économiser l'espace disque (SQLite)
        private string PreCleanHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;

            var clean = Regex.Replace(html, @"<(script|style|svg|canvas|header|footer|nav)[^>]*?>.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            return clean.Trim();
        }
    }
}
