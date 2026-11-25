using CodeHollow.FeedReader;
using Rss_feeder_prout.Helpers;
using Rss_feeder_prout.Models;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Maui.Networking;

namespace Rss_feeder_prout.Services
{
    /// <summary>
    /// Service RSS — cœur : récupération des flux, articles "en ligne", mise à jour de la base.
    /// Les autres parties sont dans :
    ///   RssService.Extraction.cs  → extraction d'article + méthodes anti-blocage / paywall d'origine
    ///   RssService.Images.cs      → images et icônes
    /// </summary>
    public partial class RssService
    {
        private readonly SQLiteService _dbService;
        private readonly ImageCacheService _images;
        private readonly HttpClient _httpClient;   // client "navigateur" (pages HTML)
        private readonly HttpClient _feedClient;   // client dédié aux flux

        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(4); // Max 4 extractions simultanées
        private readonly PriorityQueue<RssItem, int> _downloadQueue = new();
        private bool _isProcessingQueue = false;
        private readonly SemaphoreSlim _queueSemaphore = new(1, 1);

        /// <summary>Un article vient d'être téléchargé (contenu complet).</summary>
        public event EventHandler<RssItem>? ItemContentDownloaded;

        public RssService(SQLiteService dbService, ImageCacheService images)
        {
            _dbService = dbService;
            _images = images;

            // Utilisation d'un Handler pour accepter les cookies (crucial pour certains sites)
            var handler = new HttpClientHandler()
            {
                UseCookies = true,
                AutomaticDecompression = DecompressionMethods.All,
                AllowAutoRedirect = true
            };

            _httpClient = new HttpClient(handler);

            // Timeout pour éviter les blocages infinis
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            // Headers ultra-réalistes (Mode Navigateur Moderne)
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
            _httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("fr-FR,fr;q=0.9,en-US;q=0.8,en;q=0.7");
            _httpClient.DefaultRequestHeaders.Add("Referer", "https://www.google.com/");
            _httpClient.DefaultRequestHeaders.Add("sec-ch-ua", "\"Chromium\";v=\"124\", \"Not(A:Brand\";v=\"24\", \"Google Chrome\";v=\"124\"");
            _httpClient.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
            _httpClient.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"Windows\"");
            _httpClient.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");

            // Client pour les flux (Accept adapté aux formats RSS/Atom/JSON)
            var feedHandler = new HttpClientHandler
            {
                UseCookies = true,
                AutomaticDecompression = DecompressionMethods.All,
                AllowAutoRedirect = true
            };
            _feedClient = new HttpClient(feedHandler) { Timeout = TimeSpan.FromSeconds(30) };
            _feedClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Mobile Safari/537.36 RssProut/2.0");
            _feedClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/rss+xml, application/atom+xml, application/feed+json, application/json;q=0.9, application/xml;q=0.9, text/xml;q=0.8, */*;q=0.5");
            _feedClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "fr-FR,fr;q=0.9,en;q=0.8");
        }

        // ----------------------------------------------------------------------
        // RÉCUPÉRATION D'UN FLUX
        // ----------------------------------------------------------------------

        /// <summary>
        /// Télécharge et analyse un flux. useConditional = requête conditionnelle (ETag / Last-Modified)
        /// pour ne rien retélécharger si le flux n'a pas changé.
        /// </summary>
        public async Task<FeedFetchResult> FetchFeedAsync(FeedSite site, bool useConditional, CancellationToken ct = default)
        {
            var result = new FeedFetchResult();
            var url = NormalizeUrl(site.FeedUrl);
            if (string.IsNullOrEmpty(url))
            {
                result.Error = "URL du flux vide ou invalide.";
                return result;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (useConditional)
                {
                    if (!string.IsNullOrEmpty(site.ETag)) request.Headers.TryAddWithoutValidation("If-None-Match", site.ETag);
                    if (!string.IsNullOrEmpty(site.LastModified)) request.Headers.TryAddWithoutValidation("If-Modified-Since", site.LastModified);
                }

                using var response = await _feedClient.SendAsync(request, cts.Token);
                result.StatusCode = (int)response.StatusCode;
                result.FinalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;

                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    result.Success = true;
                    result.NotModified = true;
                    return result;
                }

                byte[]? bytes = null;
                string? contentType = response.Content.Headers.ContentType?.MediaType;

                if (response.IsSuccessStatusCode)
                {
                    bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
                    result.ETag = response.Headers.ETag?.ToString() ?? string.Empty;
                    result.LastModified = response.Content.Headers.LastModified?.ToString("R") ?? string.Empty;
                }
                else if (AppSettings.UsePaywallMethods && response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests)
                {
                    // Blocage anti-bot : seconde tentative avec le client "furtif" d'origine
                    using var stealth = CreateStealthHttpClient();
                    using var retry = await stealth.GetAsync(url, cts.Token);
                    result.StatusCode = (int)retry.StatusCode;
                    if (retry.IsSuccessStatusCode)
                    {
                        bytes = await retry.Content.ReadAsByteArrayAsync(cts.Token);
                        contentType = retry.Content.Headers.ContentType?.MediaType;
                    }
                }

                if (bytes == null)
                {
                    result.Error = $"HTTP {result.StatusCode} {response.ReasonPhrase}";
                    return result;
                }

                try
                {
                    result.Feed = FeedParser.Parse(bytes, contentType, result.FinalUrl);
                    result.Success = true;
                }
                catch (Exception parseEx)
                {
                    // Secours : la bibliothèque d'origine CodeHollow.FeedReader
                    try
                    {
                        var feed = await FeedReader.ReadAsync(url, cts.Token);
                        result.Feed = ConvertCodeHollowFeed(feed);
                        result.Success = true;
                    }
                    catch
                    {
                        result.Error = "Flux illisible : " + parseEx.Message;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                result.Error = "Délai dépassé (30 s).";
            }
            catch (HttpRequestException ex)
            {
                result.Error = "Réseau : " + (ex.InnerException?.Message ?? ex.Message);
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }

        private static ParsedFeed ConvertCodeHollowFeed(Feed feed)
        {
            var parsed = new ParsedFeed
            {
                Title = feed.Title ?? string.Empty,
                SiteLink = feed.Link ?? string.Empty,
                Description = feed.Description ?? string.Empty,
                IconUrl = feed.ImageUrl ?? string.Empty,
                Format = feed.Type.ToString()
            };

            foreach (var item in feed.Items)
            {
                DateTime? date = null;
                if (item.PublishingDate.HasValue)
                {
                    var d = item.PublishingDate.Value;
                    date = d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : DateTime.SpecifyKind(d, DateTimeKind.Utc);
                }

                parsed.Items.Add(new ParsedFeedItem
                {
                    Id = item.Id ?? item.Link ?? string.Empty,
                    Title = item.Title ?? string.Empty,
                    Link = item.Link ?? string.Empty,
                    SummaryHtml = item.Description ?? string.Empty,
                    ContentHtml = item.Content ?? string.Empty,
                    Author = item.Author ?? string.Empty,
                    PublishedUtc = date,
                    ImageUrl = FeedParser.FirstImageInHtml(item.Content ?? item.Description)
                });
            }
            return parsed;
        }

        public static string NormalizeUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;
            url = url.Trim().Replace(" ", "%20");
            if (url.StartsWith("feed://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url.Substring(7);
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                url = "https://" + url;
            return Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.ToString() : string.Empty;
        }

        // ----------------------------------------------------------------------
        // CONVERSION FLUX -> ARTICLE
        // ----------------------------------------------------------------------

        private static string BuildGuid(ParsedFeedItem p)
        {
            var guid = !string.IsNullOrWhiteSpace(p.Id) ? p.Id.Trim() : p.Link?.Trim();
            if (string.IsNullOrWhiteSpace(guid)) guid = FormatHelper.StableHash(p.Title + p.PublishedUtc);
            return guid.Length > 500 ? FormatHelper.StableHash(guid) : guid;
        }

        private RssItem ToRssItem(ParsedFeedItem p, FeedSite site, int playlistId)
        {
            var now = DateTime.UtcNow;
            var published = p.PublishedUtc ?? now;
            var summarySource = !string.IsNullOrWhiteSpace(p.SummaryHtml) ? p.SummaryHtml : p.ContentHtml;

            var imageUrl = p.ImageUrl ?? string.Empty;
            if (string.IsNullOrEmpty(imageUrl))
                imageUrl = ExtractImageUrlFromHtml(!string.IsNullOrEmpty(p.ContentHtml) ? p.ContentHtml : p.SummaryHtml, p.Link) ?? string.Empty;

            var item = new RssItem
            {
                ArticleGuid = BuildGuid(p),
                Title = p.Title,
                Link = p.Link ?? string.Empty,
                Summary = CleanHtmlSummary(summarySource) is { Length: > 0 } s ? s : "Aucun résumé disponible.",
                ImageUrl = imageUrl,
                Author = HtmlHelper.StripHtmlSingleLine(p.Author),
                PublishDateUtc = DateTime.SpecifyKind(published, DateTimeKind.Utc),
                PublishDate = DateTime.SpecifyKind(published, DateTimeKind.Utc).ToLocalTime().ToString("g"),
                FetchedAtUtc = now,
                PlaylistId = playlistId,
                SiteId = site.Id,
                SiteName = site.Name,
                SiteIconPath = site.LocalIconPath,
                Categories = string.Join(", ", p.Categories.Take(6)),
                ContentHtml = p.ContentHtml ?? string.Empty
            };
            item.BuildSearchBlob();
            return item;
        }

        /// <summary>Le flux contient-il déjà l'article complet ?</summary>
        private static bool FeedHasFullContent(string? contentHtml) => HtmlHelper.TextLength(contentHtml) >= 1200;

        // ----------------------------------------------------------------------
        // MISE À JOUR D'UNE SOURCE (enregistrement en base)
        // ----------------------------------------------------------------------

        /// <summary>
        /// Récupère le flux d'une source et enregistre les nouveaux articles.
        /// Retourne (nouveaux articles, succès).
        /// </summary>
        public async Task<(int newItems, bool ok)> UpdateSiteFeedAsync(FeedSite site, ImageMode thumbnailMode, CancellationToken ct = default)
        {
            site.LastFetchedUtc = DateTime.UtcNow;
            var result = await FetchFeedAsync(site, useConditional: true, ct);

            if (!result.Success)
            {
                site.ErrorCount++;
                site.LastError = result.Error;
                await _dbService.UpdateSiteAsync(site);
                Debug.WriteLine($"[FEED ERROR] {site.FeedUrl}: {result.Error}");
                return (0, false);
            }

            site.ErrorCount = 0;
            site.LastError = string.Empty;
            site.LastSuccessUtc = DateTime.UtcNow;

            if (result.NotModified || result.Feed == null)
            {
                await _dbService.UpdateSiteAsync(site);
                return (0, true);
            }

            var feed = result.Feed;
            if (!string.IsNullOrEmpty(result.ETag)) site.ETag = result.ETag;
            if (!string.IsNullOrEmpty(result.LastModified)) site.LastModified = result.LastModified;
            if (string.IsNullOrWhiteSpace(site.SiteUrl) && !string.IsNullOrWhiteSpace(feed.SiteLink)) site.SiteUrl = feed.SiteLink;
            if (string.IsNullOrWhiteSpace(site.Name) && !string.IsNullOrWhiteSpace(feed.Title)) site.Name = feed.Title.Trim();
            if (string.IsNullOrWhiteSpace(site.IconUrl) && !string.IsNullOrWhiteSpace(feed.IconUrl)) site.IconUrl = feed.IconUrl;

            // Anti-doublons
            var parsedItems = feed.Items.Where(i => !string.IsNullOrWhiteSpace(i.Link) || !string.IsNullOrWhiteSpace(i.Title)).ToList();
            var guids = parsedItems.Select(BuildGuid).ToList();
            var existing = await _dbService.GetExistingGuidsAsync(site.Id, guids);

            var newItems = new List<RssItem>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in parsedItems)
            {
                ct.ThrowIfCancellationRequested();
                var guid = BuildGuid(p);
                if (existing.Contains(guid) || !seen.Add(guid)) continue;
                newItems.Add(ToRssItem(p, site, site.PlaylistId));
            }

            // Contenu complet déjà fourni par le flux -> lisible hors-ligne immédiatement
            foreach (var item in newItems)
            {
                if (FeedHasFullContent(item.ContentHtml))
                {
                    try
                    {
                        var clean = await FinalSanitizeAndOptimizeAsync(item.ContentHtml, item.Link);
                        item.ContentHtml = clean;
                        item.IsDownloaded = !string.IsNullOrWhiteSpace(clean);
                        item.DownloadedAtUtc = DateTime.UtcNow;
                        item.ReadingMinutes = HtmlHelper.EstimateReadingMinutes(clean);
                        item.BuildSearchBlob(HtmlHelper.StripHtml(clean));
                    }
                    catch
                    {
                        item.ContentHtml = string.Empty;
                    }
                }
                else
                {
                    item.ContentHtml = string.Empty; // le résumé suffit, le complet sera téléchargé à la demande
                }
            }

            // Vignettes (image principale) en local
            if (thumbnailMode != ImageMode.None && newItems.Count > 0)
            {
                using var thumbGate = new SemaphoreSlim(4);
                var tasks = newItems.Where(i => !string.IsNullOrEmpty(i.ImageUrl)).Select(async item =>
                {
                    await thumbGate.WaitAsync(ct);
                    try
                    {
                        var local = await _images.CacheImageAsync(item.ImageUrl, ImageMode.Optimized, item.Link, ct);
                        if (!string.IsNullOrEmpty(local)) item.LocalImagePath = local;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { }
                    finally { thumbGate.Release(); }
                }).ToList();
                await Task.WhenAll(tasks);
            }

            int inserted = await _dbService.InsertNewItemsAsync(newItems);
            await _dbService.UpdateSiteAsync(site);
            return (inserted, true);
        }

        // ----------------------------------------------------------------------
        // ARTICLES "EN LIGNE" (non enregistrés)
        // ----------------------------------------------------------------------

        /// <summary>
        /// NOUVEAU : lit les flux en direct et retourne les articles qui ne sont PAS dans la base
        /// (rien n'est enregistré). Ils s'affichent avec le badge « En ligne ».
        /// </summary>
        public async Task<List<RssItem>> FetchLiveItemsAsync(IEnumerable<FeedSite> sites, CancellationToken ct = default)
        {
            var result = new List<RssItem>();
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet) return result;

            var siteList = sites.Where(s => !s.IsDisabled).ToList();
            using var gate = new SemaphoreSlim(6);
            var lockObj = new object();

            var tasks = siteList.Select(async site =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    var fetch = await FetchFeedAsync(site, useConditional: false, ct);
                    if (!fetch.Success || fetch.Feed == null) return;

                    var items = fetch.Feed.Items.Select(p => ToRssItem(p, site, site.PlaylistId)).ToList();
                    var existing = await _dbService.GetExistingGuidsAsync(site.Id, items.Select(i => i.ArticleGuid));
                    var fresh = items.Where(i => !existing.Contains(i.ArticleGuid))
                                     .GroupBy(i => i.ArticleGuid).Select(g => g.First())
                                     .ToList();
                    foreach (var i in fresh) i.IsLive = true;

                    lock (lockObj) result.AddRange(fresh);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LIVE] {site.FeedUrl}: {ex.Message}");
                }
                finally
                {
                    gate.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks);
            return result.OrderByDescending(i => i.PublishDateUtc).ToList();
        }

        /// <summary>Enregistre un article "en ligne" dans la base (avec ou sans contenu complet).</summary>
        public async Task<RssItem?> SaveLiveItemAsync(RssItem live, bool downloadFull, ImageMode imageMode, string? preExtractedHtml = null, CancellationToken ct = default)
        {
            if (live.Id > 0) return live;

            var toSave = new RssItem
            {
                ArticleGuid = live.ArticleGuid,
                Title = live.Title,
                Summary = live.Summary,
                Link = live.Link,
                Author = live.Author,
                ImageUrl = live.ImageUrl,
                PublishDate = live.PublishDate,
                PublishDateUtc = live.PublishDateUtc,
                FetchedAtUtc = DateTime.UtcNow,
                PlaylistId = live.PlaylistId,
                SiteId = live.SiteId,
                SiteName = live.SiteName,
                Categories = live.Categories,
                IsRead = live.IsRead,
                IsFavorite = live.IsFavorite,
                IsReadLater = live.IsReadLater
            };
            toSave.BuildSearchBlob();

            var stored = await _dbService.SaveLiveItemAsync(toSave);
            if (stored == null) return null;

            live.Id = stored.Id;
            live.IsLive = false;

            if (toSave.IsFavorite) await _dbService.SetFavoriteAsync(stored, true);
            if (toSave.IsReadLater) await _dbService.SetReadLaterAsync(stored, true);
            if (toSave.IsRead) await _dbService.SetReadAsync(stored, true);

            if (!string.IsNullOrWhiteSpace(preExtractedHtml))
            {
                await SaveExtractedContentAsync(stored, preExtractedHtml, imageMode, ct);
            }
            else if (downloadFull)
            {
                await DownloadAndSaveItemContentAsync(stored, imageMode, ct);
            }
            else if (FeedHasFullContent(live.ContentHtml))
            {
                var clean = await FinalSanitizeAndOptimizeAsync(live.ContentHtml, live.Link);
                await SaveExtractedContentAsync(stored, clean, imageMode, ct);
            }

            live.IsDownloaded = stored.IsDownloaded;
            live.ImagesCached = stored.ImagesCached;
            return stored;
        }

        // ----------------------------------------------------------------------
        // MÉTHODES D'ORIGINE (conservées, réécrites sur le nouveau moteur)
        // ----------------------------------------------------------------------

        /// <summary>
        /// Retourne les articles en cache d'une catégorie / source et lance une mise à jour en tâche de fond.
        /// </summary>
        public async Task<List<RssItem>> UpdateAndGetFeedItemsForSitesAsync(FeedPlaylist playlist, FeedSite? site = null)
        {
            if (playlist == null)
                return new List<RssItem>();

            List<FeedSite> sitesToFetch = site != null
                ? new List<FeedSite> { site }
                : await _dbService.GetSitesForPlaylistAsync(playlist.Id);

            var cachedItems = await _dbService.GetItemsForPlaylistAsync(playlist.Id, site?.Id);

            if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet && sitesToFetch.Any())
            {
                _ = Task.Run(async () => await PerformFeedUpdateAsync(playlist.Id, sitesToFetch));
            }
            else
            {
                Debug.WriteLine("Mode Hors-ligne : Chargement depuis la base SQLite.");
            }

            return cachedItems;
        }

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
                    try { await DownloadAndSaveItemContentAsync(item, AppSettings.ManualImageMode); }
                    catch (Exception ex) { Debug.WriteLine($"[QUEUE] {ex.Message}"); }
                }
            }
            finally
            {
                _isProcessingQueue = false;
                _queueSemaphore.Release();
            }
        }

        /// <summary>Synchronise une catégorie : flux + contenu complet (réglages manuels).</summary>
        public async Task SynchronizeSitesInPlaylistAsync(int playlistId, CancellationToken ct = default)
        {
            Debug.WriteLine($"[SYNC] Début de la synchronisation complète pour la playlist ID: {playlistId}");

            var sitesToFetch = await _dbService.GetSitesByPlaylistIdAsync(playlistId);
            if (sitesToFetch == null || !sitesToFetch.Any()) return;

            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                throw new InvalidOperationException("Connexion Internet requise.");

            await PerformFeedUpdateAsync(playlistId, sitesToFetch, ct);
            if (AppSettings.ManualDownloadContent)
                await DownloadAllContentForPlaylistAsync(playlistId, null, ct);
        }

        /// <summary>Met à jour plusieurs sources en parallèle. Retourne le nombre de nouveaux articles.</summary>
        public async Task<int> PerformFeedUpdateAsync(int playlistId, List<FeedSite> sitesToFetch, CancellationToken ct = default)
        {
            int total = 0;
            using var gate = new SemaphoreSlim(6);
            var tasks = sitesToFetch.Where(s => !s.IsDisabled).Select(async site =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    var (n, _) = await UpdateSiteFeedAsync(site, AppSettings.ManualImageMode, ct);
                    Interlocked.Add(ref total, n);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Debug.WriteLine($"[GLOBAL UPDATE ERROR] {ex.Message}"); }
                finally { gate.Release(); }
            }).ToList();

            await Task.WhenAll(tasks);
            return total;
        }

        /// <summary>Télécharge le contenu complet de tous les articles non téléchargés d'une catégorie.</summary>
        public async Task DownloadAllContentForPlaylistAsync(int playlistId, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            var itemsToDownload = await _dbService.GetItemsToDownloadAsync(new[] { playlistId }, AppSettings.MaxArticlesPerSync);
            if (itemsToDownload.Count == 0) return;
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet) return;

            int done = 0;
            int total = itemsToDownload.Count;
            using var gate = new SemaphoreSlim(3);

            var tasks = itemsToDownload.Select(async item =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    await DownloadAndSaveItemContentAsync(item, AppSettings.ManualImageMode, ct);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Debug.WriteLine($"Erreur critique sur {item.Title}: {ex.Message}"); }
                finally
                {
                    Interlocked.Increment(ref done);
                    progress?.Report((double)done / total);
                    gate.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks);
        }

        // ----------------------------------------------------------------------
        // DÉCOUVERTE / TEST DE FLUX
        // ----------------------------------------------------------------------

        /// <summary>
        /// NOUVEAU : à partir d'une URL de site OU de flux, trouve les flux disponibles
        /// (balises &lt;link rel="alternate"&gt; + emplacements courants).
        /// </summary>
        public async Task<List<DiscoveredFeed>> DiscoverFeedsAsync(string input, CancellationToken ct = default)
        {
            var found = new List<DiscoveredFeed>();
            var url = NormalizeUrl(input);
            if (string.IsNullOrEmpty(url)) return found;

            // 1. C'est peut-être déjà un flux
            var direct = await FetchFeedAsync(new FeedSite { FeedUrl = url }, false, ct);
            if (direct.Success && direct.Feed != null)
            {
                found.Add(new DiscoveredFeed { Url = url, Title = direct.Feed.Title, Type = direct.Feed.Format });
                return found;
            }

            // 2. Page HTML : on cherche les liens alternatifs
            try
            {
                var html = await _httpClient.GetStringAsync(url, ct);
                var baseUri = new Uri(url);
                foreach (Match m in Regex.Matches(html, @"<link\b[^>]*>", RegexOptions.IgnoreCase))
                {
                    var tag = m.Value;
                    if (!Regex.IsMatch(tag, @"rel\s*=\s*[""']?alternate", RegexOptions.IgnoreCase)) continue;
                    var type = Regex.Match(tag, @"type\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase).Groups[1].Value;
                    if (!type.Contains("rss") && !type.Contains("atom") && !type.Contains("feed+json") && !type.Contains("rdf")) continue;
                    var href = Regex.Match(tag, @"href\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase).Groups[1].Value;
                    if (string.IsNullOrWhiteSpace(href)) continue;
                    var title = Regex.Match(tag, @"title\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase).Groups[1].Value;
                    if (Uri.TryCreate(baseUri, WebUtility.HtmlDecode(href), out var abs) && !found.Any(f => f.Url == abs.ToString()))
                        found.Add(new DiscoveredFeed { Url = abs.ToString(), Title = WebUtility.HtmlDecode(title), Type = type });
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DISCOVER] {ex.Message}");
            }

            if (found.Count > 0) return found;

            // 3. Emplacements courants
            var root = new Uri(url).GetLeftPart(UriPartial.Authority);
            foreach (var suffix in new[] { "/feed", "/rss", "/rss.xml", "/feed.xml", "/atom.xml", "/index.xml", "/feed/", "/rss/", "/feeds/posts/default" })
            {
                ct.ThrowIfCancellationRequested();
                var candidate = root + suffix;
                var test = await FetchFeedAsync(new FeedSite { FeedUrl = candidate }, false, ct);
                if (test.Success && test.Feed != null && test.Feed.Items.Count > 0)
                {
                    found.Add(new DiscoveredFeed { Url = candidate, Title = test.Feed.Title, Type = test.Feed.Format });
                    break;
                }
            }

            return found;
        }

        /// <summary>Teste une source et met à jour son état de santé.</summary>
        public async Task<(bool ok, string message)> TestFeedAsync(FeedSite site, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var r = await FetchFeedAsync(site, false, ct);
            sw.Stop();

            site.LastFetchedUtc = DateTime.UtcNow;
            if (r.Success && r.Feed != null)
            {
                site.ErrorCount = 0;
                site.LastError = string.Empty;
                site.LastSuccessUtc = DateTime.UtcNow;
                if (site.Id > 0) await _dbService.UpdateSiteAsync(site);
                var last = r.Feed.Items.Where(i => i.PublishedUtc.HasValue).Select(i => i.PublishedUtc!.Value).DefaultIfEmpty().Max();
                var lastText = last == default ? "date inconnue" : DateHelper.ToRelative(DateTime.SpecifyKind(last, DateTimeKind.Utc).ToLocalTime());
                return (true, $"{r.Feed.Format} · {r.Feed.Items.Count} articles · dernier : {lastText} · {sw.ElapsedMilliseconds} ms");
            }

            site.ErrorCount++;
            site.LastError = r.Error;
            if (site.Id > 0) await _dbService.UpdateSiteAsync(site);
            return (false, r.Error);
        }

        // ----------------------------------------------------------------------
        // MÉTHODES UTILITAIRES (Inchangées)
        // ----------------------------------------------------------------------

        private string CleanHtmlSummary(string? summary, int maxLength = 400)
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
