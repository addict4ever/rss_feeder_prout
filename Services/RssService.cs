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

            // Récupère les articles en cache pour l'affichage initial
            var cachedItems = await _dbService.GetItemsForPlaylistAsync(playlist.Id, site?.Id);

            bool isConnected = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

            if (isConnected && sitesToFetch.Any())
            {
                // Démarre la mise à jour en tâche de fond pour ne pas bloquer l'UI
                // On utilise PerformFeedUpdateAsync, qui est le cœur de la logique de synchronisation.
                _ = Task.Run(async () =>
                {
                    await PerformFeedUpdateAsync(playlist.Id, sitesToFetch);
                });
            }
            else if (!isConnected)
            {
                Debug.WriteLine("Offline mode: Only loading from SQLite cache.");
            }

            // Retourne immédiatement le cache, même si la MAJ est en cours
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

        public async Task SynchronizeSitesInPlaylistAsync(int playlistId)
        {
            Debug.WriteLine($"[SYNC] Début de la synchronisation complète pour la playlist ID: {playlistId}");

            // 1. Récupération des sites (Vérification du nom de la méthode selon ton SQLiteService)
            var sitesToFetch = await _dbService.GetSitesByPlaylistIdAsync(playlistId);

            if (sitesToFetch == null || !sitesToFetch.Any())
            {
                Debug.WriteLine($"[SYNC] Aucun site trouvé pour la playlist {playlistId}.");
                return;
            }

            // 2. Vérification de la connexion
            bool isConnected = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

            if (isConnected)
            {
                try
                {
                    // A. Mise à jour des flux RSS
                    // Cette étape trouve les nouveaux articles (titres, liens) et les insère en DB
                    Debug.WriteLine($"[SYNC] Étape 1 : Mise à jour des flux RSS pour {sitesToFetch.Count} sites...");
                    await PerformFeedUpdateAsync(playlistId, sitesToFetch);

                    // B. Téléchargement du contenu HTML complet
                    // C'est cette étape qui permet le mode Offline en allant scrapper chaque page web
                    Debug.WriteLine($"[SYNC] Étape 2 : Téléchargement du contenu HTML complet pour le mode hors-ligne...");
                    await DownloadAllContentForPlaylistAsync(playlistId);

                    Debug.WriteLine($"[SYNC] Terminé avec succès pour la playlist {playlistId}.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SYNC ERROR] Erreur pendant la synchronisation : {ex.Message}");
                    throw; // On remonte l'erreur pour que le ViewModel puisse l'afficher
                }
            }
            else
            {
                Debug.WriteLine("[SYNC] Échec : Pas de connexion Internet.");
                throw new InvalidOperationException("Impossible de synchroniser. Aucune connexion Internet n'est disponible.");
            }
        }

        public async Task<string> DownloadAndSaveIconAsync(string url, string siteName)
        {
            try
            {
                using var client = new HttpClient();
                // Optionnel : Ajouter un User-Agent pour éviter d'être bloqué
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                var bytes = await client.GetByteArrayAsync(url);

                // Nettoyage du nom de fichier
                string safeName = string.Join("_", siteName.Split(Path.GetInvalidFileNameChars()));
                string fileName = $"icon_{safeName}_{Guid.NewGuid().ToString().Substring(0, 4)}.png";
                string localPath = Path.Combine(FileSystem.AppDataDirectory, fileName);

                await File.WriteAllBytesAsync(localPath, bytes);
                return localPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DOWNLOAD ERROR] : {ex.Message}");
                return null;
            }
        }

        private async Task<string> DownloadImageToLocalAsync(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl)) return null;

            try
            {
                // Créer un nom de fichier unique basé sur le Hash de l'URL pour éviter les doublons
                string fileExtension = ".jpg"; // Par défaut
                if (imageUrl.Contains(".png")) fileExtension = ".png";
                if (imageUrl.Contains(".webp")) fileExtension = ".webp";

                string fileName = $"img_{Math.Abs(imageUrl.GetHashCode())}{fileExtension}";
                string localPath = Path.Combine(FileSystem.AppDataDirectory, fileName);

                // Si l'image existe déjà localement, on retourne simplement le chemin
                if (File.Exists(localPath)) return localPath;

                // Téléchargement des octets de l'image
                byte[] imageBytes = await _httpClient.GetByteArrayAsync(imageUrl);
                await File.WriteAllBytesAsync(localPath, imageBytes);

                return localPath; // Retourne le chemin local (ex: /data/user/0/.../img_123.jpg)
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IMAGE CACHE ERROR] {imageUrl} : {ex.Message}");
                return imageUrl; // En cas d'échec, on garde l'URL web pour ne pas casser l'affichage
            }
        }


        // ----------------------------------------------------------------------
        // --- CŒUR DE LA LOGIQUE DE MISE À JOUR (Inchangée) ---
        // ----------------------------------------------------------------------

        private async Task PerformFeedUpdateAsync(int playlistId, List<FeedSite> sitesToFetch)
        {
            Debug.WriteLine($"Starting feed update for playlist {playlistId}...");
            try
            {
                var allNewItems = new List<RssItem>();
                // Récupère les GUIDs existants pour éviter les doublons
                var existingGuids = await _dbService.GetCachedArticleGuidsAsync(playlistId);

                // 1. Récupération des flux en parallèle
                var fetchTasks = sitesToFetch.Select(async currentSite =>
                {
                    try
                    {
                        var feed = await FeedReader.ReadAsync(currentSite.FeedUrl);
                        return (Site: currentSite, Items: feed.Items);
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
                    var currentSite = result.Site;

                    // Filtrage immédiat des articles que nous avons déjà en DB (gain de performance)
                    var filteredFeedItems = result.Items
                        .Where(item => !existingGuids.Contains(item.Id ?? item.Link))
                        .ToList();

                    foreach (var item in filteredFeedItems)
                    {
                        // --- LOGIQUE D'IMAGE ---
                        string rawImageUrl = ExtractImageUrlFromHtml(item.Content ?? item.Description);
                        string localImageUrl = rawImageUrl;

                        // TÉLÉCHARGEMENT OFFINE : On télécharge l'image avant de créer l'objet
                        if (!string.IsNullOrEmpty(rawImageUrl))
                        {
                            localImageUrl = await DownloadImageToLocalAsync(rawImageUrl);
                        }

                        string author = item.Author ?? string.Empty;

                        var newItem = new RssItem
                        {
                            ArticleGuid = item.Id ?? item.Link,
                            Title = item.Title,
                            Link = item.Link,
                            Summary = CleanHtmlSummary(item.Description) ?? "Aucun résumé disponible.",
                            ImageUrl = localImageUrl, // Ici on stocke le chemin LOCAL
                            Author = author,
                            PublishDate = item.PublishingDate.HasValue
                                ? item.PublishingDate.Value.ToString("g")
                                : "N/A",
                            PlaylistId = playlistId,
                            SiteId = currentSite.Id,
                            ContentHtml = item.Content,
                            IsDownloaded = !string.IsNullOrWhiteSpace(item.Content)
                        };

                        allNewItems.Add(newItem);
                        existingGuids.Add(newItem.ArticleGuid);
                    }
                }

                // 2. Sauvegarde groupée en base de données
                if (allNewItems.Any())
                {
                    await _dbService.SaveItemsAsync(allNewItems);
                    Debug.WriteLine($"Successfully saved {allNewItems.Count} new items with local images.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GLOBAL UPDATE ERROR] {ex.Message}");
                throw;
            }
        }

        // ---------------------------------------------------------------
        // --- LOGIQUE DE TÉLÉCHARGEMENT DE CONTENU HORS LIGNE (BRUT) (Inchangée) ---
        // ---------------------------------------------------------------


        public async Task DownloadAndSaveItemContentAsync(RssItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Link)) return;
            // On évite de retélécharger si c'est déjà fait
            if (item.IsDownloaded && !string.IsNullOrEmpty(item.ContentHtml)) return;

            await _semaphore.WaitAsync();

            try
            {
                using var client = CreateStealthHttpClient();

                // 2. Téléchargement initial
                string htmlContent = await FetchRawContentAsync(client, item.Link);
                if (string.IsNullOrEmpty(htmlContent) || htmlContent.Contains("cf-browser-verification")) return;

                var config = Configuration.Default.WithDefaultLoader();
                using var context = BrowsingContext.New(config);
                var document = await context.OpenAsync(req => req.Content(htmlContent));

                // 4. Navigation profonde (Bypass "Lire la suite")
                var deepDocument = await HandleDeepNavigationAsync(client, document, item.Link);

                // CRUCIAL : Si on a navigué plus loin, on met à jour la source HTML pour l'extracteur
                if (deepDocument != document)
                {
                    document = deepDocument;
                    htmlContent = document.Source.Text; // On récupère le nouveau HTML complet
                }

                // 5. Bypass Paywall & Anti-Blur
                ApplyStructuralBypass(document);

                // 6. Extraction Heuristique (On utilise le HTML mis à jour)
                string mainContentHtml = await ExtractMainContentAsync(document, item.Link, htmlContent);

                // 7. Nettoyage agressif
                string finalCleanHtml = await FinalSanitizeAndOptimizeAsync(mainContentHtml, item.Link);

                // 8. Sauvegarde et mise à jour de l'objet
                if (!string.IsNullOrWhiteSpace(finalCleanHtml))
                {
                    await _dbService.SaveArticleContentAsync(item, finalCleanHtml);

                    // Mise à jour de l'objet en mémoire pour l'affichage immédiat
                    item.ContentHtml = finalCleanHtml;
                    item.IsDownloaded = true;

                    Debug.WriteLine($"[SUCCESS] Article prêt pour : {item.Title}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CRITICAL ERROR] {item.Link} : {ex.Message}");
            }
            finally
            {
                _semaphore.Release();
            }
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
            var candidates = document.QuerySelectorAll("a, button, div.read-more, .pagination a, .next-page")
                .Where(el => {
                    var text = el.TextContent?.ToLower() ?? "";
                    var className = el.ClassName?.ToLower() ?? "";
                    var id = el.Id?.ToLower() ?? "";

                    return text.Contains("suite") || text.Contains("lecture") ||
                           text.Contains("complet") || text.Contains("continuer") ||
                           className.Contains("read-more") || className.Contains("next") ||
                           id.Contains("btn-more");
                }).ToList();

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
                    // Si on détecte une "Page suivante", on peut techniquement fusionner (optionnel)
                    // Pour l'instant, on retourne le nouveau document comme source principale
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

        private void ApplyStructuralBypass(IDocument document)
        {
            // 1. LISTE NOIRE ÉLARGIE (Sélecteurs complexes et Patterns)
            var restrictiveSelectors = new[] {
        "[class*='paywall']", "[id*='paywall']", "[class*='gate']", "[class*='barrier']",
        "[class*='premium']", "[class*='overlay']", "[class*='modal']", "[class*='popup']",
        "[class*='blocking']", "[class*='subscription']", "[class*='reg-wall']",
        ".tp-modal", ".tp-backdrop", ".fc-ab-root", ".is-locked", ".sp-wall",
        "aside[class*='social']", "div[class*='newsletter']", "iframe[src*='facebook']"
    };

            // A. Suppression chirurgicale des éléments de blocage
            foreach (var selector in restrictiveSelectors)
            {
                var elements = document.QuerySelectorAll(selector);
                foreach (var el in elements) el.Remove();
            }

            // 2. NEUTRALISATION DES "HONEY-POTS" ET ÉLÉMENTS INVISIBLES
            // On supprime les trackers et les éléments de détection de bot
            var traps = document.QuerySelectorAll("*").Where(e =>
                e.GetAttribute("style")?.Replace(" ", "").Contains("display:none") == true ||
                e.GetAttribute("aria-hidden") == "true" ||
                e.InnerHtml.Length == 0 && e.TagName != "IMG" && e.TagName != "BR"
            );
            foreach (var t in traps) t.Remove();

            // 3. ANTI-BLUR ET ANTI-GRAYSCALE (Restauration visuelle totale)
            // Certains sites floutent ou mettent en gris le texte sous le paywall
            var hiddenByCss = document.QuerySelectorAll("*").Where(e =>
                e.GetAttribute("style")?.Contains("filter") == true ||
                e.GetAttribute("style")?.Contains("opacity") == true ||
                e.GetAttribute("style")?.Contains("webkit-filter") == true);

            foreach (var el in hiddenByCss)
            {
                el.SetAttribute("style",
                    "filter: none !important; " +
                    "-webkit-filter: none !important; " +
                    "opacity: 1 !important; " +
                    "visibility: visible !important; " +
                    "display: block !important;");
            }

            // 4. RESTAURATION DU SCROLL (Bypass "Overflow Lock")
            // Les sites bloquent souvent le défilement pour forcer l'abonnement
            var scrollBlockers = document.QuerySelectorAll("html, body");
            foreach (var node in scrollBlockers)
            {
                node.SetAttribute("style",
                    "overflow: visible !important; " +
                    "position: static !important; " +
                    "height: auto !important; " +
                    "display: block !important; " +
                    "-webkit-overflow-scrolling: touch !important; " +
                    "pointer-events: auto !important;");
            }

            // 5. NETTOYAGE DES Z-INDEX (Suppression des couches de surface)
            // On vire les Divs vides ou promotionnelles qui flottent au-dessus du texte
            var overlays = document.QuerySelectorAll("div").Where(d =>
            {
                var style = d.GetAttribute("style") ?? "";
                return style.Contains("z-index") && !d.QuerySelectorAll("p").Any();
            });
            foreach (var ov in overlays) ov.Remove();

            // 6. DÉVERROUILLAGE DES ATTRIBUTS DE SÉCURITÉ
            // Certains sites ajoutent des classes "locked" sur le conteneur principal
            var containers = document.QuerySelectorAll(".article-container, .post-content, #article-body");
            foreach (var container in containers)
            {
                container.RemoveAttribute("aria-hidden");
                container.SetAttribute("style", "opacity: 1 !important; display: block !important;");
            }

            // 7. SUPPRESSION DES SCRIPTS DE DÉTECTION (Ultime recours)
            // On enlève les balises <script> qui pourraient redéclencher le paywall
            foreach (var script in document.QuerySelectorAll("script")) script.Remove();
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





        public async Task DownloadAllContentForPlaylistAsync(int playlistId, IProgress<double> progress = null)
        {
            var itemsToDownload = await _dbService.GetItemsToDownloadAsync(playlistId);

            if (!itemsToDownload.Any()) return;

            // 1. Vérification de la connexion (Optionnel mais recommandé)
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet) return;

            const int maxConcurrentDownloads = 4;
            using var semaphore = new SemaphoreSlim(maxConcurrentDownloads);
            int downloadedCount = 0;
            int totalCount = itemsToDownload.Count;

            var downloadTasks = itemsToDownload.Select(async item =>
            {
                await semaphore.WaitAsync();
                try
                {
                    if (string.IsNullOrWhiteSpace(item.Link)) return;

                    // 2. Téléchargement avec timeout pour éviter de bloquer la queue
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    var response = await _httpClient.GetAsync(item.Link, cts.Token);

                    if (response.IsSuccessStatusCode)
                    {
                        string rawHtml = await response.Content.ReadAsStringAsync();

                        // 3. NETTOYAGE EXPRESS (On ne garde que l'essentiel pour la base de données)
                        // On utilise la logique de nettoyage simplifiée ici pour gagner de la place
                        item.ContentHtml = PreCleanHtml(rawHtml);
                        item.IsDownloaded = true;
                    }
                }
                catch (Exception ex)
                {
                    item.IsDownloaded = false;
                    Debug.WriteLine($"Erreur sur {item.Title}: {ex.Message}");
                }
                finally
                {
                    downloadedCount++;
                    // 4. Mise à jour de la barre de progression
                    progress?.Report((double)downloadedCount / totalCount);
                    semaphore.Release();
                }
            }).ToList();

            await Task.WhenAll(downloadTasks);

            // 5. SAUVEGARDE PAR LOTS (Transactionnelle)
            var successItems = itemsToDownload.Where(i => i.IsDownloaded).ToList();
            if (successItems.Any())
            {
                await _dbService.UpdateItemsWithContentAsync(successItems);
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