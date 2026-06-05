using CodeHollow.FeedReader;
using Rss_feeder_prout.Models;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Networking;
using System.Net.Http;
using System.Text.RegularExpressions;
using System;
using System.Net;
using System.Threading;
using AngleSharp;
using AngleSharp.Dom;
using SmartReader; // Ajoute ceci
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text;

namespace Rss_feeder_prout.Services
{
    public class RssService
    {
        private readonly SQLiteService _dbService;
        private readonly HttpClient _httpClient;

        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(5); // Point 8: Max 5 téléchargements simultanés
        private readonly PriorityQueue<RssItem, int> _downloadQueue = new();
        private bool _isProcessingQueue = false;
        private readonly SemaphoreSlim _queueSemaphore = new(1, 1);

        public RssService(SQLiteService dbService)
        {
            _dbService = dbService;

            // Utilisation d'un Handler pour accepter les cookies (crucial pour certains sites)
            var handler = new HttpClientHandler()
            {
                UseCookies = true,
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };

            _httpClient = new HttpClient(handler);

            // Point 2: Timeout pour éviter les blocages infinis
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            // Point 6: Headers ultra-réalistes (Mode Navigateur Moderne)
            // On vide les headers par défaut pour éviter les conflits
            _httpClient.DefaultRequestHeaders.Clear();

            // User-Agent : On simule un Chrome récent sur Windows 10
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");

            // Accept : Indique qu'on accepte le HTML et les formats d'images modernes
            _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");

            // Langues : Priorité au Français
            _httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("fr-FR,fr;q=0.9,en-US;q=0.8,en;q=0.7");

            // Referer : Certains sites vérifient d'où vient la requête (on peut simuler Google)
            _httpClient.DefaultRequestHeaders.Add("Referer", "https://www.google.com/");

            // Sec-Fetch : Headers de sécurité modernes que les navigateurs envoient maintenant
            _httpClient.DefaultRequestHeaders.Add("sec-ch-ua", "\"Chromium\";v=\"122\", \"Not(A:Brand\";v=\"24\", \"Google Chrome\";v=\"122\"");
            _httpClient.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
            _httpClient.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"Windows\"");
            _httpClient.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
        }

        // ----------------------------------------------------------------------
        // --- LOGIQUE PRINCIPALE : MISE À JOUR DU FLUX RSS (Mise en cache) ---
        // ----------------------------------------------------------------------

        public async Task<List<RssItem>> UpdateAndGetFeedItemsForSitesAsync(FeedPlaylist playlist, FeedSite site = null)
        {
            if (playlist == null)
                return new List<RssItem>();

            List<FeedSite> sitesToFetch;

            if (site != null)
            {
                sitesToFetch = new List<FeedSite> { site };
            }
            else
            {
                sitesToFetch = await _dbService.GetSitesForPlaylistAsync(playlist.Id);
            }

            // 1. Récupère les articles en cache (Lit TOUS les fichiers horodatés via le service)
            // IMPORTANT : Ton service _dbService.GetItemsForPlaylistAsync doit maintenant scanner 
            // tous les fichiers RssProut_*.db3 pour trouver les articles de cette playlist.
            var cachedItems = await _dbService.GetItemsForPlaylistAsync(playlist.Id, site?.Id);

            bool isConnected = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

            if (isConnected && sitesToFetch.Any())
            {
                // 2. Démarre la mise à jour en tâche de fond
                _ = Task.Run(async () =>
                {
                    // Cette fonction utilise maintenant ItemExistsInAnyDatabaseAsync
                    // Elle ne téléchargera QUE ce qui n'est dans AUCUNE base.
                    await PerformFeedUpdateAsync(playlist.Id, sitesToFetch);

                    // OPTIONNEL : Envoyer un message (MessagingCenter) pour dire à l'UI 
                    // de se rafraîchir maintenant que de nouveaux articles sont arrivés.
                });
            }
            else if (!isConnected)
            {
                Debug.WriteLine("Mode Hors-ligne : Chargement depuis les archives SQLite.");
            }

            // 3. Retourne immédiatement ce qu'on a trouvé dans tous les fichiers .db3
            return cachedItems;
        }

        // ----------------------------------------------------------------------
        // ✅ MODIFIÉ : LOGIQUE DE SYNCHRONISATION SPÉCIFIQUE (Pour le bouton Sync All)
        // ----------------------------------------------------------------------

        /// <summary>
        /// Synchronise tous les sites d'une playlist, met à jour les flux ET TÉLÉCHARGE LE CONTENU COMPLET.
        /// Utilisé par la commande "Sync All" du MainViewModel.
        /// </summary>
        /// 
        public void EnqueueDownload(RssItem item, int priority = 1)
        {
            lock (_downloadQueue)
            {
                _downloadQueue.Enqueue(item, priority);
            }
            _ = ProcessQueueAsync(); // Lance le traitement sans bloquer l'UI
        }

        private async Task ProcessQueueAsync()
        {
            await _queueSemaphore.WaitAsync();
            try
            {
                if (_isProcessingQueue) return;
                _isProcessingQueue = true;

                while (true)
                {
                    RssItem item;
                    lock (_downloadQueue)
                    {
                        if (_downloadQueue.Count == 0) break;
                        item = _downloadQueue.Dequeue();
                    }
                    await DownloadAndSaveItemContentAsync(item);
                }
            }
            finally
            {
                _isProcessingQueue = false;
                _queueSemaphore.Release();
            }
        }

        public async Task SynchronizeSitesInPlaylistAsync(int playlistId, CancellationToken ct = default)
        {
            Debug.WriteLine($"[SYNC] Début de la synchronisation complète pour la playlist ID: {playlistId}");

            var sitesToFetch = await _dbService.GetSitesByPlaylistIdAsync(playlistId);

            if (sitesToFetch == null || !sitesToFetch.Any())
            {
                Debug.WriteLine($"[SYNC] Aucun site trouvé pour la playlist {playlistId}.");
                return;
            }

            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                Debug.WriteLine("[SYNC] Échec : Pas de connexion Internet.");
                throw new InvalidOperationException("Connexion Internet requise.");
            }

            try
            {
                // On propage le 'ct' à chaque étape
                Debug.WriteLine($"[SYNC] Étape 1 : Récupération des flux RSS...");
                await PerformFeedUpdateAsync(playlistId, sitesToFetch, ct);

                Debug.WriteLine($"[SYNC] Étape 2 : Téléchargement du contenu HTML complet...");
                await DownloadAllContentForPlaylistAsync(playlistId, null, ct);

                Debug.WriteLine($"[SYNC] Terminé avec succès pour la playlist {playlistId}.");
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[SYNC] Synchronisation annulée pour la playlist {playlistId}.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SYNC ERROR] : {ex.Message}");
                throw;
            }
        }



        // Ajoute CancellationToken ct = default à la fin
        public async Task<string> DownloadAndSaveIconAsync(string url, string siteName, CancellationToken ct = default)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                // Passe le token à GetByteArrayAsync
                // Si l'utilisateur clique sur "Stop", cette ligne lancera une OperationCanceledException
                var bytes = await client.GetByteArrayAsync(url, ct);

                string safeName = string.Join("_", siteName.Split(Path.GetInvalidFileNameChars()));
                string fileName = $"icon_{safeName}_{Guid.NewGuid().ToString().Substring(0, 4)}.png";
                string localPath = Path.Combine(FileSystem.AppDataDirectory, fileName);

                // Optionnel : tu peux aussi utiliser WriteAllBytesAsync avec le token si tu utilises des librairies spécifiques, 
                // mais pour le fichier local c'est généralement instantané.
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

        private async Task<string> CacheHtmlImagesLocallyAsync(IDocument document, string baseUrl)
        {
            if (document == null) return string.Empty;

            var images = document.QuerySelectorAll("img[src]");
            var baseUri = new Uri(baseUrl);

            foreach (var img in images)
            {
                string originalSrc = img.GetAttribute("src");
                if (string.IsNullOrWhiteSpace(originalSrc)) continue;

                // 1. Convertir en URL absolue si nécessaire
                string absoluteUrl = originalSrc.StartsWith("http")
                    ? originalSrc
                    : new Uri(baseUri, originalSrc).AbsoluteUri;

                // 2. Télécharger l'image localement
                string localPath = await DownloadImageToLocalAsync(absoluteUrl);

                // 3. Remplacer la source par le chemin local
                // Sur Android/iOS, le chemin local doit souvent être préfixé par "file://" pour le WebView
                if (!string.IsNullOrEmpty(localPath) && !localPath.StartsWith("http"))
                {
                    img.SetAttribute("src", $"file://{localPath}");
                }
            }

            return document.Body.InnerHtml;
        }

        private async Task<string> DownloadImageToLocalAsync(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl)) return null;

            try
            {
                // Extraction de l'extension de manière plus propre
                string extension = Path.GetExtension(new Uri(imageUrl).AbsolutePath);
                if (string.IsNullOrEmpty(extension)) extension = ".jpg";

                // Création d'un nom unique (Hash)
                string fileName = $"cache_{Math.Abs(imageUrl.GetHashCode())}{extension}";
                string localPath = Path.Combine(FileSystem.AppDataDirectory, fileName);

                if (File.Exists(localPath)) return localPath;

                // Utilisation d'une requête manuelle pour s'assurer d'avoir les bons headers
                using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    byte[] imageBytes = await response.Content.ReadAsByteArrayAsync();
                    await File.WriteAllBytesAsync(localPath, imageBytes);
                    return localPath;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IMAGE ERROR] {imageUrl} : {ex.Message}");
            }
            return imageUrl; // Retourne l'original en cas d'échec
        }


        // ----------------------------------------------------------------------
        // --- CŒUR DE LA LOGIQUE DE MISE À JOUR (Inchangée) ---
        // ----------------------------------------------------------------------

        private async Task PerformFeedUpdateAsync(int playlistId, List<FeedSite> sitesToFetch, CancellationToken ct = default)
        {
            Debug.WriteLine($"Starting global feed update for playlist {playlistId}...");
            try
            {
                var allNewItems = new List<RssItem>();

                // 1. Récupération des flux en parallèle avec gestion du token
                var fetchTasks = sitesToFetch.Select(async currentSite =>
                {
                    // Vérification avant de commencer chaque tâche
                    if (ct.IsCancellationRequested) return (Site: currentSite, Items: Enumerable.Empty<FeedItem>());

                    try
                    {
                        // On passe le token à FeedReader
                        var feed = await FeedReader.ReadAsync(currentSite.FeedUrl, ct);
                        return (Site: currentSite, Items: feed.Items);
                    }
                    catch (OperationCanceledException)
                    {
                        return (Site: currentSite, Items: Enumerable.Empty<FeedItem>());
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[FEED ERROR] {currentSite.FeedUrl}: {ex.Message}");
                        return (Site: currentSite, Items: Enumerable.Empty<FeedItem>());
                    }
                }).ToList();

                var results = await Task.WhenAll(fetchTasks);

                foreach (var result in results.Where(r => r.Items.Any()))
                {
                    // Vérification à chaque itération de flux
                    if (ct.IsCancellationRequested) break;

                    var currentSite = result.Site;

                    foreach (var item in result.Items)
                    {
                        // Vérification à chaque article
                        if (ct.IsCancellationRequested) break;

                        string articleGuid = item.Id ?? item.Link;

                        bool alreadyExists = await _dbService.IsArticleAlreadyDownloadedAsync(articleGuid);
                        if (alreadyExists) continue;

                        // --- LOGIQUE D'IMAGE ---
                        string rawImageUrl = ExtractImageUrlFromHtml(item.Content ?? item.Description);
                        string localImageUrl = rawImageUrl;

                        if (!string.IsNullOrEmpty(rawImageUrl))
                        {
                            // Optionnel : tu pourrais aussi passer le 'ct' à DownloadImageToLocalAsync si cette méthode est longue
                            localImageUrl = await DownloadImageToLocalAsync(rawImageUrl);
                        }

                        string author = item.Author ?? string.Empty;

                        var newItem = new RssItem
                        {
                            ArticleGuid = articleGuid,
                            Title = item.Title,
                            Link = item.Link,
                            Summary = CleanHtmlSummary(item.Description) ?? "Aucun résumé disponible.",
                            ImageUrl = localImageUrl,
                            Author = author,
                            PublishDate = item.PublishingDate.HasValue
                                ? item.PublishingDate.Value.ToString("g")
                                : DateTime.Now.ToString("g"),
                            PlaylistId = playlistId,
                            SiteId = currentSite.Id,
                            ContentHtml = item.Content,
                            IsDownloaded = !string.IsNullOrWhiteSpace(item.Content)
                        };

                        await _dbService.AddToArticleIndexAsync(articleGuid);
                        allNewItems.Add(newItem);
                    }
                }

                // 2. SAUVEGARDE
                if (allNewItems.Any() && !ct.IsCancellationRequested)
                {
                    await _dbService.SaveItemsAsync(allNewItems);
                    Debug.WriteLine($"Successfully saved {allNewItems.Count} new items.");
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Feed update was cancelled.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GLOBAL UPDATE ERROR] {ex.Message}");
            }
        }

        public async Task DownloadAndSaveItemContentAsync(RssItem item, CancellationToken ct = default)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Link)) return;
            if (item.IsDownloaded && !string.IsNullOrEmpty(item.ContentHtml)) return;

            string cleanedUrl = item.Link.Trim().Replace(" ", "%20");
            if (!cleanedUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;

            var currentSite = await _dbService.GetSiteByIdAsync(item.SiteId ?? 0);
            await _semaphore.WaitAsync(ct);

            try
            {
                using var client = CreateStealthHttpClient();

                // 1. Téléchargement du HTML brut
                var response = await client.GetAsync(cleanedUrl, ct);
                if (!response.IsSuccessStatusCode) return;

                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                string htmlContent = EnsureUtf8Encoding(bytes, response.Content.Headers.ContentType?.CharSet);

                if (string.IsNullOrEmpty(htmlContent)) return;

                // 2. Pré-nettoyage (Regex)
                htmlContent = PreParseSanitize(htmlContent);

                // 3. Parsing AngleSharp
                var config = Configuration.Default.WithDefaultLoader();
                using var context = BrowsingContext.New(config);
                using var document = await context.OpenAsync(req => req.Content(htmlContent), ct);

                // 4. Navigation profonde et réparation des URLs
                var deepDocument = await HandleDeepNavigationAsync(client, document, cleanedUrl);
                var activeDocument = (deepDocument != null && deepDocument != document) ? deepDocument : document;

                FixRelativeUrls(activeDocument, cleanedUrl);

                // 5. Extraction du contenu principal
                string mainContentHtml = "";
                if (currentSite != null && !string.IsNullOrEmpty(currentSite.CustomCleanSelectors))
                {
                    var element = activeDocument.QuerySelector(currentSite.CustomCleanSelectors);
                    mainContentHtml = element?.InnerHtml ?? "";
                }

                if (string.IsNullOrWhiteSpace(mainContentHtml))
                {
                    mainContentHtml = await ExtractMainContentAsync(activeDocument, cleanedUrl, htmlContent);
                }

                // 🎯 ÉTAPE CRUCIALE : Téléchargement des images en local pour le mode Offline
                // On repasse le contenu extrait dans AngleSharp pour traiter les images une par une
                mainContentHtml = await ProcessAndCacheImagesAsync(mainContentHtml, cleanedUrl);

                // 6. Nettoyage final et optimisation
                string finalCleanHtml = await FinalSanitizeAndOptimizeAsync(mainContentHtml, cleanedUrl);

                // 7. Sauvegarde en base de données
                if (!string.IsNullOrWhiteSpace(finalCleanHtml) && finalCleanHtml.Length > 100)
                {
                    await _dbService.SaveArticleContentAsync(item, finalCleanHtml);
                    item.ContentHtml = finalCleanHtml;
                    item.IsDownloaded = true;
                    Debug.WriteLine($"[OFFLINE READY] Texte et Images stockés pour : {item.Title}");
                }

                if (deepDocument != null && deepDocument != document) deepDocument.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CRITICAL ERROR] {cleanedUrl} : {ex.Message}");
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task<string> ProcessAndCacheImagesAsync(string html, string baseUrl)
        {
            if (string.IsNullOrEmpty(html)) return html;

            var config = Configuration.Default;
            using var context = BrowsingContext.New(config);
            using var doc = await context.OpenAsync(req => req.Content(html));

            var images = doc.QuerySelectorAll("img");
            foreach (var img in images)
            {
                string src = img.GetAttribute("src");
                if (string.IsNullOrEmpty(src)) continue;

                // Télécharger et obtenir le chemin local
                string localPath = await DownloadImageToLocalAsync(src);

                if (!string.IsNullOrEmpty(localPath))
                {
                    // On remplace l'URL web par le chemin du fichier local
                    // Note: Sur certains WebView, il faut préfixer par "file://"
                    img.SetAttribute("src", $"file://{localPath}");
                }
            }
            return doc.Body.InnerHtml;
        }


        private string EnsureUtf8Encoding(byte[] bytes, string charset)
        {
            try
            {
                if (charset != null && charset.Contains("iso-8859", StringComparison.OrdinalIgnoreCase))
                {
                    // Utilise l'encodage Europe de l'Ouest pour les vieux sites
                    return Encoding.GetEncoding("iso-8859-1").GetString(bytes);
                }
            }
            catch { }
            return Encoding.UTF8.GetString(bytes);
        }

        private void FixRelativeUrls(IDocument document, string baseUrl)
        {
            if (document == null || string.IsNullOrEmpty(baseUrl)) return;
            var baseUri = new Uri(baseUrl);

            // Réparer les images (src="/img.jpg" -> src="http://site.com/img.jpg")
            foreach (var img in document.QuerySelectorAll("img[src]"))
            {
                var src = img.GetAttribute("src");
                if (!string.IsNullOrEmpty(src) && !src.StartsWith("http"))
                    img.SetAttribute("src", new Uri(baseUri, src).AbsoluteUri);
            }

            // Réparer les liens
            foreach (var a in document.QuerySelectorAll("a[href]"))
            {
                var href = a.GetAttribute("href");
                if (!string.IsNullOrEmpty(href) && !href.StartsWith("http") && !href.StartsWith("#"))
                    a.SetAttribute("href", new Uri(baseUri, href).AbsoluteUri);
            }
        }

        private string PreParseSanitize(string html)
        {
            if (string.IsNullOrEmpty(html)) return html;

            // Retire les balises lourdes qui font ramer le parsing
            html = Regex.Replace(html, @"<script\b[^>]*>([\s\S]*?)</script>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<style\b[^>]*>([\s\S]*?)</style>", "", RegexOptions.IgnoreCase);

            // Coupe le HTML inutile après l'article (très efficace pour VDM et blogs)
            if (html.Contains("</article>", StringComparison.OrdinalIgnoreCase))
            {
                html = Regex.Replace(html, @"</article>.*", "</article>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            }

            return html;
        }

        private async Task<string> FetchRawContentAsync(HttpClient client, string link)
        {
            try
            {
                // 1. GESTION DU TIMEOUT ET TENTATIVE DE RÉCUPÉRATION
                // On utilise GetAsync pour inspecter les headers avant de lire tout le contenu
                using var response = await client.GetAsync(link, HttpCompletionOption.ResponseHeadersRead);

                // Si le serveur nous bloque (403) ou ne trouve pas (404)
                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[AVERTISSEMENT] Code {response.StatusCode} pour {link}");
                    // On peut tenter un dernier recours ici si besoin
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

                    // Analyse profonde du JSON pour trouver du contenu HTML
                    // On cherche dans "content_html" ou "content_text" ou "summary"
                    if (root.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
                    {
                        var firstItem = items[0];
                        if (firstItem.TryGetProperty("content_html", out var html)) return html.GetString();
                        if (firstItem.TryGetProperty("content_text", out var text)) return $"<p>{text.GetString()}</p>";
                    }

                    // Si c'est un format JSON différent (ex: API spécifique)
                    if (root.TryGetProperty("content", out var directContent)) return directContent.GetString();

                    return string.Empty;
                }

                // 4. TRAITEMENT HTML AVEC AUTO-DÉTECTION DE L'ENCODAGE
                // Certains sites utilisent ISO-8859-1 au lieu de UTF-8 (accents brisés sinon)
                var bytes = await response.Content.ReadAsByteArrayAsync();
                var charset = response.Content.Headers.ContentType?.CharSet;

                Encoding encoding = Encoding.UTF8; // Par défaut
                try
                {
                    if (!string.IsNullOrEmpty(charset)) encoding = Encoding.GetEncoding(charset);
                }
                catch { encoding = Encoding.UTF8; }

                string htmlContent = encoding.GetString(bytes);

                // 5. VÉRIFICATION DE SÉCURITÉ (Anti-Bot Check)
                if (htmlContent.Contains("Cloudflare") && htmlContent.Contains("votre navigateur"))
                {
                    Debug.WriteLine("[ALERTE] Détecté par Cloudflare sur : " + link);
                    // Ici, on pourrait déclencher une logique de proxy plus agressive
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


        private async Task<IDocument> HandleDeepNavigationAsync(HttpClient client, IDocument document, string originalLink)
        {
            var rnd = new Random();

            // 1. RECHERCHE ÉLARGIE (Sélecteurs de déclenchement sémantiques)
            // On cherche non seulement dans les liens, mais aussi les éléments typiques des portails de presse
            var keywords = new[]
            {
        // FR
        "suite", "lire la suite", "lecture", "article complet", "continuer",
        "continuer la lecture", "en savoir plus", "voir l'article", "plus",

        // EN (très fréquent dans RSS feeds)
        "read more", "continue reading", "full article", "see full article",
        "more", "load more", "show more",

        // navigation feed
        "next", "next page", "previous", "prev", "older", "newer",
        "older posts", "newer posts"
    };

            var candidates = document.QuerySelectorAll("a, button, div, span, li, article, nav, section, p")
                .Where(el =>
                {
                    var text = (el.TextContent ?? "").ToLowerInvariant();
                    var className = (el.ClassName ?? "").ToLowerInvariant();
                    var id = (el.Id ?? "").ToLowerInvariant();

                    var href = el.GetAttribute("href")?.ToLowerInvariant() ?? "";
                    var aria = el.GetAttribute("aria-label")?.ToLowerInvariant() ?? "";
                    var title = el.GetAttribute("title")?.ToLowerInvariant() ?? "";
                    var data = el.GetAttribute("data-testid")?.ToLowerInvariant() ?? "";

                    bool matchKeywords = keywords.Any(k =>
                        text.Contains(k) ||
                        className.Contains(k) ||
                        id.Contains(k) ||
                        aria.Contains(k) ||
                        title.Contains(k) ||
                        href.Contains(k) ||
                        data.Contains(k)
                    );

                    // détection RSS/pagination structurelle
                    bool looksLikeFeedNav =
                        className.Contains("pagination") ||
                        className.Contains("pager") ||
                        className.Contains("feed") ||
                        className.Contains("post") ||
                        className.Contains("entry") ||
                        className.Contains("article") ||
                        className.Contains("nav") ||
                        className.Contains("more") ||
                        className.Contains("next") ||
                        className.Contains("older") ||
                        className.Contains("newer");

                    // éviter faux positifs (liens sociaux, ads)
                    bool blacklist =
                        className.Contains("facebook") ||
                        className.Contains("twitter") ||
                        className.Contains("instagram") ||
                        className.Contains("ad") ||
                        className.Contains("promo");

                    return (matchKeywords || looksLikeFeedNav) && !blacklist;
                })
                .Distinct()
                .ToList();

            // On prend le premier candidat qui a un attribut de lien
            var trigger = candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c.GetAttribute("href") ?? c.GetAttribute("data-url")));

            if (trigger != null)
            {
                string nextUrl = trigger.GetAttribute("href") ?? trigger.GetAttribute("data-url");

                // Sécurité : éviter les boucles infinies ou les liens vers les réseaux sociaux
                if (string.IsNullOrEmpty(nextUrl) || nextUrl.Contains("facebook.com") || nextUrl.Contains("twitter.com"))
                    return document;

                // Reconstruction intelligente de l'URL
                string fullUrl = nextUrl.StartsWith("http") ? nextUrl : new Uri(new Uri(originalLink), nextUrl).AbsoluteUri;

                // --- SIMULATION DE COMPORTEMENT HUMAIN ---
                // On attend un délai crédible (lecture du premier paragraphe)
                await Task.Delay(rnd.Next(2000, 4500));

                try
                {
                    // Mise à jour des headers pour simuler le clic
                    client.DefaultRequestHeaders.Referrer = new Uri(originalLink);
                    client.DefaultRequestHeaders.Remove("Sec-Fetch-Site");
                    client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");

                    // Récupération de la page complète
                    string newHtml = await client.GetStringAsync(fullUrl);

                    // Vérification si le nouveau contenu est réellement plus long (éviter les redirections inutiles)
                    if (newHtml.Length < document.Source.Text.Length * 0.8)
                        return document;

                    var config = Configuration.Default.WithDefaultLoader();
                    var context = BrowsingContext.New(config);
                    var nextDocument = await context.OpenAsync(req => req.Content(newHtml));

                    // 2. GESTION DE LA PAGINATION (Articles en plusieurs pages)
                    Debug.WriteLine($"[DEEP NAV] Passage au contenu complet : {fullUrl}");
                    return nextDocument;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DEBUG] Échec de la navigation profonde : {ex.Message}");
                    return document;
                }
            }

            // 3. AUTO-DETECTION DES "IFRAMES" DE CONTENU (Certains paywalls utilisent des cadres)
            var iframeContent = document.QuerySelector("iframe.article-content, iframe[src*='news']");
            if (iframeContent != null)
            {
                string iframeSrc = iframeContent.GetAttribute("src");
                if (!string.IsNullOrEmpty(iframeSrc))
                {
                    string fullIframeUrl = iframeSrc.StartsWith("http") ? iframeSrc : new Uri(new Uri(originalLink), iframeSrc).AbsoluteUri;
                    string iframeHtml = await client.GetStringAsync(fullIframeUrl);
                    return await BrowsingContext.New(Configuration.Default).OpenAsync(req => req.Content(iframeHtml));
                }
            }

            return document;
        }
        private HttpClient CreateStealthHttpClient()
        {
            var rnd = new Random();

            // 1. Dictionnaires de navigation pour cohérence (UA + Client Hints)
            // Chaque entrée est un profil cohérent pour ne pas envoyer un UA de Chrome avec des indices de Firefox
            var browserProfiles = new[]
            {
        new {
            UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36",
            CH_UA = "\"Google Chrome\";v=\"123\", \"Not:A-Brand\";v=\"8\", \"Chromium\";v=\"123\"",
            Platform = "\"Windows\"",
            Mobile = "?0"
        },
        new {
            UA = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36",
            CH_UA = "\"Google Chrome\";v=\"123\", \"Not:A-Brand\";v=\"8\", \"Chromium\";v=\"123\"",
            Platform = "\"macOS\"",
            Mobile = "?0"
        },
        new {
            UA = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36",
            CH_UA = "\"Google Chrome\";v=\"123\", \"Not:A-Brand\";v=\"8\", \"Chromium\";v=\"123\"",
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
            client.DefaultRequestHeaders.Add("sec-ch-ua", profile.CH_UA);
            client.DefaultRequestHeaders.Add("sec-ch-ua-mobile", profile.Mobile);
            client.DefaultRequestHeaders.Add("sec-ch-ua-platform", profile.Platform);

            // C. Capacités du navigateur (Acceptation de formats compressés et modernes)
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
            client.DefaultRequestHeaders.Add("Accept-Language", "fr-FR,fr;q=0.9,en-US;q=0.8,en;q=0.7");
            client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");

            // D. Headers de Navigation (Simulation d'une requête utilisateur réelle)
            client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
            client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
            client.DefaultRequestHeaders.Add("DNT", "1"); // Do Not Track

            // E. Gestion du Cache (Pour ne pas avoir l'air d'un bot qui rafraîchit en boucle)
            client.DefaultRequestHeaders.Add("Cache-Control", "max-age=0");
            client.DefaultRequestHeaders.Add("Connection", "keep-alive");

            // F. Obfuscation d'IP (X-Forwarded-For avec IP résidentielle aléatoire factice)
            string fakeIp = $"{rnd.Next(1, 255)}.{rnd.Next(1, 255)}.{rnd.Next(1, 255)}.{rnd.Next(1, 255)}";
            client.DefaultRequestHeaders.Add("X-Forwarded-For", fakeIp);

            return client;
        }

        private void AutoAcceptCookies(IDocument document)
        {
            // Sélecteurs courants pour les bannières de consentement (Radio-Canada, etc.)
            var consentSelectors = new[]
            {
        "button[aria-label*='Accepter']",
        "button:contains('Accepter')",
        "button:contains('Tout accepter')",
        "a:contains('Accepter')",
        "#onetrust-accept-btn-handler", // OneTrust (très commun)
        ".consent-accept",
        "button[id*='accept']"
    };

            foreach (var selector in consentSelectors)
            {
                var btn = document.QuerySelector(selector);
                if (btn != null)
                {
                    Debug.WriteLine($"[CONSENT] Bannière détectée : clic simulé sur {selector}");
                    // On ne peut pas "cliquer" réellement, mais on supprime la bannière 
                    // pour que SmartReader ne voie que le contenu principal
                    btn.Remove();

                    // Si c'est une bannière Radio-Canada, on force aussi le retrait du overlay
                    var overlay = document.QuerySelector(".modal-backdrop, .overlay, #blocking-layer");
                    overlay?.Remove();

                    // On rétablit le scroll immédiatement
                    document.Body.RemoveAttribute("class");
                    document.Body.SetAttribute("style", "overflow: auto !important;");
                }
            }
        }

        private string ApplyStructuralBypass(IDocument document, FeedSite site)
        {
            if (document == null) return string.Empty;

            AutoAcceptCookies(document);

            // 1. SUPPRESSION DES TAGS INUTILES (Rapide et léger)
            var nuisanceTags = new[] {
        "script", "noscript", "style", "iframe", "nav", "footer", "header",
        "aside", "form", "button", "svg", "canvas", "meta", "link", "noscript",
        "embed", "object", "script", "noscript"
    };
            foreach (var tag in nuisanceTags)
            {
                foreach (var el in document.QuerySelectorAll(tag).ToList()) el.Remove();
            }

            // 2. NEUTRALISATION DES TRAPS & ÉLÉMENTS INVISIBLES (Optimisé)
            // Au lieu de '*', on cible directement les attributs suspects
            var traps = document.QuerySelectorAll("[style*='display:none'], [aria-hidden='true']");
            foreach (var t in traps) { try { t.Remove(); } catch { } }

            // 3. ANTI-BLUR ET ANTI-GRAYSCALE
            // On cible seulement les éléments ayant un style avec filtre ou opacité
            var hiddenByCss = document.QuerySelectorAll("[style*='filter'], [style*='opacity'], [style*='webkit-filter']");
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

            var allBlocks = document.QuerySelectorAll("div, section, article, main, p");

            // On filtre pour ne garder que les conteneurs sérieux (ceux qui ont une vraie structure)
            var bestCandidates = allBlocks
                .Where(e => {
                    int textLength = e.TextContent?.Trim().Length ?? 0;
                    // On ne veut pas de petits éléments comme des boutons ou des spans isolés
                    return textLength > 200 && e.QuerySelectorAll("p, h1, h2, h3").Any();
                })
                .OrderByDescending(e => e.TextContent?.Length ?? 0)
                .Take(3) // On prend les 3 plus gros blocs de texte trouvés
                .ToList();

            if (bestCandidates.Any())
            {
                // On crée un conteneur racine pour SmartReader
                var wrapper = document.CreateElement("div");
                wrapper.SetAttribute("id", "rss-prout-extracted-content");

                foreach (var candidate in bestCandidates)
                {
                    // On vérifie si ce bloc n'est pas déjà un enfant d'un autre bloc sélectionné
                    // pour éviter de dupliquer le contenu (doublons)
                    if (!bestCandidates.Any(other => other != candidate && other.Contains(candidate)))
                    {
                        wrapper.AppendChild(candidate);
                    }
                }

                // On remplace le Body par notre wrapper propre
                document.Body.InnerHtml = string.Empty;
                document.Body.AppendChild(wrapper);
            }

            // 6. SMARTREADER (Le travail final d'extraction)
            try
            {
                var reader = new SmartReader.Reader(document.DocumentElement.OuterHtml);
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
            var reader = new SmartReader.Reader(url, rawHtml);
            var article = await reader.GetArticleAsync();

            // Si SmartReader réussit avec un bon volume de texte, on valide
            if (article != null && article.IsReadable && article.Content.Length > 1000)
                return article.Content;

            // 2. ANALYSE DE DENSITÉ TEXTUELLE (Algorithme maison sophistiqué)
            // On cherche le conteneur qui possède le meilleur ratio Texte/Balises
            var candidates = document.QuerySelectorAll("div, section, article, main")
                .Select(e => {
                    // Calcul du score de densité
                    int textLength = e.TextContent?.Trim().Length ?? 0;
                    int htmlLength = e.InnerHtml?.Length ?? 1; // Éviter division par zéro
                    double density = (double)textLength / htmlLength;

                    // Bonus pour les structures typiques d'articles
                    int pCount = e.QuerySelectorAll("p").Length;
                    int imgCount = e.QuerySelectorAll("img").Length;

                    double finalScore = textLength * density; // Score de base
                    if (pCount >= 2) finalScore *= 1.5; // Bonus paragraphes
                    if (e.TagName.ToLower() == "article") finalScore *= 1.2; // Bonus sémantique

                    // Malus pour les zones de liens (menus, footers)
                    int linkLength = e.QuerySelectorAll("a").Sum(a => a.TextContent.Length);
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
                var fallbackSelectors = new[] { ".post-content", ".article-body", "#entry-content", ".entry-content-wrapper" };
                foreach (var selector in fallbackSelectors)
                {
                    var el = document.QuerySelector(selector);
                    if (el != null && el.TextContent.Length > 200)
                    {
                        extractedHtml = el.InnerHtml;
                        break;
                    }
                }
            }

            // Si toujours rien, on prend le body nettoyé
            if (string.IsNullOrEmpty(extractedHtml)) extractedHtml = document.Body.InnerHtml;

            // 4. RÉCUPÉRATION DES MÉTADONNÉES DE SECOURS (Images & Titres)
            // Si l'image principale manque, on va la chercher dans les balises OpenGraph
            var featuredImg = article?.FeaturedImage;
            if (string.IsNullOrEmpty(featuredImg))
            {
                featuredImg = document.QuerySelector("meta[property='og:image']")?.GetAttribute("content")
                            ?? document.QuerySelector("link[rel='image_src']")?.GetAttribute("href");
            }

            // 5. ASSEMBLAGE FINAL (Injection propre)
            string finalResult = "";

            // Ajout du titre s'il manque dans le contenu extrait
            if (!extractedHtml.Contains(article?.Title ?? ""))
            {
                finalResult += $"<h1 style='font-size:1.5em; margin-bottom:10px;'>{article?.Title}</h1>";
            }

            // Injection de l'image de couverture stylisée
            if (!string.IsNullOrEmpty(featuredImg))
            {
                finalResult += $"<img src='{featuredImg}' style='width:100%; border-radius:15px; margin-bottom:20px; box-shadow: 0 4px 8px rgba(0,0,0,0.1);' />";
            }

            finalResult += extractedHtml;

            return finalResult;
        }

        private async Task<string> FinalSanitizeAndOptimizeAsync(string html, string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;

            var config = Configuration.Default;
            var context = BrowsingContext.New(config);
            var doc = await context.OpenAsync(req => req.Content(html));

            var authorizedTags = new HashSet<string> {
        "P", "H1", "H2", "H3", "H4", "BR", "B", "I", "STRONG", "EM",
        "UL", "OL", "LI", "IMG", "A", "BLOCKQUOTE", "FIGURE", "FIGCAPTION", "HR"
    };

            // On récupère tous les éléments du corps
            var allElements = doc.Body.QuerySelectorAll("*").ToList();

            foreach (var el in allElements)
            {
                // Si la balise n'est pas dans la liste autorisée
                if (!authorizedTags.Contains(el.TagName))
                {
                    // Liste des balises dont on veut garder le texte (conteneurs)
                    if (el.TagName == "DIV" || el.TagName == "SECTION" || el.TagName == "SPAN" || el.TagName == "ARTICLE")
                    {
                        // SOLUTION DE SECOURS POUR UNWRAP :
                        // On déplace tous les enfants de l'élément actuel juste avant lui dans le DOM
                        while (el.FirstChild != null)
                        {
                            el.Before(el.FirstChild);
                        }
                        el.Remove(); // On supprime la balise vide
                    }
                    else
                    {
                        el.Remove(); // Pour les balises inutiles (script, style, etc.), on supprime tout
                    }
                    continue;
                }

                // Nettoyage des attributs (on ne garde que l'essentiel)
                var attrNames = el.Attributes.Select(a => a.Name).ToList();
                foreach (var attr in attrNames)
                {
                    if (attr != "src" && attr != "href" && attr != "title" && attr != "alt")
                    {
                        el.RemoveAttribute(attr);
                    }
                }
            }

            // --- LE RESTE DU CODE (IMAGES ET LIENS) RESTE LE MÊME ---
            // (Traitement des images avec le proxy Google et les liens)

            foreach (var img in doc.QuerySelectorAll("img").Cast<AngleSharp.Html.Dom.IHtmlImageElement>())
            {
                var realSrc = img.GetAttribute("data-src") ?? img.GetAttribute("data-lazy-src") ?? img.Source;
                if (!string.IsNullOrEmpty(realSrc))
                {
                    string absUrl = new Uri(new Uri(baseUrl), realSrc).AbsoluteUri;
                    img.Source = $"https://images1-focus-opensocial.googleusercontent.com/gadgets/proxy?container=focus&refresh=2592000&url={Uri.EscapeDataString(absUrl)}";
                    img.SetAttribute("style", "max-width:100%; height:auto; border-radius:12px; margin:20px auto; display:block;");
                }
            }

            foreach (var link in doc.QuerySelectorAll("a").Cast<AngleSharp.Html.Dom.IHtmlAnchorElement>())
            {
                if (string.IsNullOrEmpty(link.Href))
                {
                    // Si le lien est vide, on fait un Unwrap manuel aussi
                    while (link.FirstChild != null) link.Before(link.FirstChild);
                    link.Remove();
                    continue;
                }
                link.SetAttribute("target", "_blank");
                link.SetAttribute("style", "color: #2196F3; text-decoration: none; font-weight: bold;");
            }

            return Regex.Replace(doc.Body.InnerHtml, @"\s+", " ").Trim();
        }





        public async Task DownloadAllContentForPlaylistAsync(int playlistId, IProgress<double> progress = null, CancellationToken ct = default)
        {
            var itemsToDownload = await _dbService.GetItemsToDownloadAsync(playlistId);

            if (itemsToDownload == null || !itemsToDownload.Any()) return;

            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet) return;

            const int maxConcurrentDownloads = 2; // Réduit à 2 pour tester la stabilité
            using var semaphore = new SemaphoreSlim(maxConcurrentDownloads);
            int downloadedCount = 0;
            int totalCount = itemsToDownload.Count;

            // Création d'une liste de tâches
            var downloadTasks = itemsToDownload.Select(async item =>
            {
                if (ct.IsCancellationRequested) return;

                await semaphore.WaitAsync(ct);
                try
                {
                    if (string.IsNullOrWhiteSpace(item.Link)) return;

                    // Utilisation d'un Timeout court pour le HttpClient
                    using var ctsTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, ctsTimeout.Token);

                    var response = await _httpClient.GetAsync(item.Link, linkedCts.Token);

                    if (response.IsSuccessStatusCode)
                    {
                        string rawHtml = await response.Content.ReadAsStringAsync();

                        // Création d'un contexte local pour chaque page (évite les conflits)
                        var config = Configuration.Default;
                        var context = BrowsingContext.New(config);
                        var document = await context.OpenAsync(req => req.Content(rawHtml), linkedCts.Token);

                        var site = await _dbService.GetSiteByIdAsync(item.SiteId ?? 0);

                        // Appel de ta méthode sécurisée (qui contient ses propres try-catch)
                        ApplyStructuralBypass(document, site);

                        item.ContentHtml = document.Body.InnerHtml;
                        item.IsDownloaded = true;
                    }
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine($"Annulé : {item.Title}");
                }
                catch (Exception ex)
                {
                    item.IsDownloaded = false;
                    Debug.WriteLine($"Erreur critique sur {item.Title}: {ex.Message}");
                }
                finally
                {
                    // Protection : on s'assure de ne jamais bloquer le compteur
                    Interlocked.Increment(ref downloadedCount);
                    progress?.Report((double)downloadedCount / totalCount);
                    semaphore.Release();
                }
            });

            await Task.WhenAll(downloadTasks);

            if (!ct.IsCancellationRequested)
            {
                var successItems = itemsToDownload.Where(i => i.IsDownloaded).ToList();
                if (successItems.Any())
                {
                    await _dbService.UpdateItemsWithContentAsync(successItems);
                }
            }
        }

        // Méthode de pré-nettoyage pour économiser l'espace disque (SQLite)
        private string PreCleanHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;

            // Supprime les blocs énormes et inutiles AVANT de mettre en base de données
            var clean = Regex.Replace(html, @"<(script|style|svg|canvas|header|footer|nav)[^>]*?>.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            return clean.Trim();
        }


        // --------------------------------------------------
        // --- MÉTHODES UTILITAIRES (Inchangées) ---
        // --------------------------------------------------

        private string ExtractImageUrlFromHtml(string html, string articleUrl = null)
        {
            if (string.IsNullOrWhiteSpace(html)) return null;

            try
            {
                // 1. Chercher d'abord les attributs de Lazy Loading (souvent la vraie image HD)
                var lazyMatch = Regex.Match(html, @"(?:data-src|data-lazy-src|srcset)\s*=\s*['""]([^'""\s?]+)", RegexOptions.IgnoreCase);
                string src = lazyMatch.Success ? lazyMatch.Groups[1].Value : null;

                // 2. Sinon, prendre le src classique
                if (string.IsNullOrEmpty(src))
                {
                    var match = Regex.Match(html, @"<img\s+[^>]*src\s*=\s*['""]([^'""]+)['""]", RegexOptions.IgnoreCase);
                    src = match.Success ? match.Groups[1].Value : null;
                }

                if (string.IsNullOrEmpty(src) || src.Contains("pixel.wp.com") || src.EndsWith(".gif")) return null;

                // 3. Normaliser l'URL (rendre absolue)
                if (!string.IsNullOrEmpty(articleUrl) && !src.StartsWith("http"))
                {
                    src = new Uri(new Uri(articleUrl), src).AbsoluteUri;
                }

                // 4. Appliquer le Proxy Google pour bypasser les blocages de "Hotlinking"
                return $"https://images1-focus-opensocial.googleusercontent.com/gadgets/proxy?container=focus&refresh=2592000&url={Uri.EscapeDataString(src)}";
            }
            catch { return null; }
        }

        private string CleanHtmlSummary(string summary, int maxLength = 400)
        {
            if (string.IsNullOrWhiteSpace(summary)) return string.Empty;

            // 1. Supprimer le HTML
            string cleanText = Regex.Replace(summary, "<[^>]*>", " ");

            // 2. Décoder les entités (ex: &eacute; -> é)
            cleanText = WebUtility.HtmlDecode(cleanText);

            // 3. Nettoyer les espaces (enlève doubles espaces, tabulations et retours chariots)
            cleanText = Regex.Replace(cleanText, @"\s+", " ").Trim();

            if (cleanText.Length <= maxLength) return cleanText;

            // 4. Couper proprement sans casser de mot
            string truncated = cleanText.Substring(0, maxLength);
            int lastSpace = truncated.LastIndexOf(' ');

            if (lastSpace > 0)
            {
                return truncated.Substring(0, lastSpace).Trim() + "...";
            }

            return truncated + "...";
        }
    }
}