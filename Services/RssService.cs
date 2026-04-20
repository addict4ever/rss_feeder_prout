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

namespace Rss_feeder_prout.Services
{
    public class RssService
    {
        private readonly SQLiteService _dbService;
        private readonly HttpClient _httpClient;

        public RssService(SQLiteService dbService)
        {
            _dbService = dbService;
            _httpClient = new HttpClient();
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


        // ----------------------------------------------------------------------
        // --- CŒUR DE LA LOGIQUE DE MISE À JOUR (Inchangée) ---
        // ----------------------------------------------------------------------

        private async Task PerformFeedUpdateAsync(int playlistId, List<FeedSite> sitesToFetch)
        {
            Debug.WriteLine($"Starting feed update for playlist {playlistId}...");
            try
            {
                var allNewItems = new List<RssItem>();
                // Récupère les GUIDs existants pour éviter les doublons avant de faire les appels réseau
                var existingGuids = await _dbService.GetCachedArticleGuidsAsync(playlistId);

                // Utiliser Task.WhenAll pour paralléliser la récupération des flux
                var fetchTasks = sitesToFetch.Select(async currentSite =>
                {
                    try
                    {
                        var feed = await FeedReader.ReadAsync(currentSite.FeedUrl);
                        return (Site: currentSite, Items: feed.Items);
                    }
                    catch (HttpRequestException ex)
                    {
                        Debug.WriteLine($"[FEED ERROR] HTTP/Network error for {currentSite.FeedUrl}: {ex.Message}. Skipping.");
                        return (Site: currentSite, Items: Enumerable.Empty<FeedItem>());
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[FEED ERROR] Parsing or unexpected error fetching RSS from {currentSite.FeedUrl}: {ex.Message}. Skipping.");
                        return (Site: currentSite, Items: Enumerable.Empty<FeedItem>());
                    }
                }).ToList();

                var results = await Task.WhenAll(fetchTasks);

                foreach (var result in results.Where(r => r.Items.Any()))
                {
                    var currentSite = result.Site;
                    var feedItems = result.Items;

                    var newItems = feedItems
                        .Select(item =>
                        {
                            // --- LOGIQUE D'IMAGE ET D'AUTEUR ---
                            string imageUrl = ExtractImageUrlFromHtml(item.Content ?? item.Description);
                            string author = item.Author ?? string.Empty;

                            return new RssItem
                            {
                                ArticleGuid = item.Id ?? item.Link,
                                Title = item.Title,
                                Link = item.Link,
                                Summary = CleanHtmlSummary(item.Description) ?? "Aucun résumé disponible.",
                                ImageUrl = imageUrl,
                                Author = author,
                                PublishDate = item.PublishingDate.HasValue
                                ? item.PublishingDate.Value.ToString("g")
                                : "N/A",
                                PlaylistId = playlistId,
                                SiteId = currentSite.Id,
                                ContentHtml = item.Content,
                                // Marquer comme téléchargé si le contenu est déjà dans le flux RSS (cas rare)
                                IsDownloaded = !string.IsNullOrWhiteSpace(item.Content)
                            };
                        })
                        .Where(item => !existingGuids.Contains(item.ArticleGuid)) // Filtrage des doublons
                        .ToList();

                    allNewItems.AddRange(newItems);
                    existingGuids.AddRange(newItems.Select(i => i.ArticleGuid));
                }

                if (allNewItems.Any())
                {
                    await _dbService.SaveItemsAsync(allNewItems);
                    Debug.WriteLine($"Successfully saved {allNewItems.Count} new items.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GLOBAL UPDATE ERROR] Failed during feed update: {ex.Message}");
                throw; // Renvoyer l'erreur pour la gestion dans SynchronizeSitesInPlaylistAsync
            }
        }

        // ---------------------------------------------------------------
        // --- LOGIQUE DE TÉLÉCHARGEMENT DE CONTENU HORS LIGNE (BRUT) (Inchangée) ---
        // ---------------------------------------------------------------

        /// <summary>
        /// Télécharge le contenu HTML complet de l'article pour la lecture hors ligne.
        /// </summary>
        public async Task DownloadAndSaveItemContentAsync(RssItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Link)) return;

            try
            {
                // 1. Télécharger la page entière
                string htmlContent = await _httpClient.GetStringAsync(item.Link);

                // 2. SAUVEGARDE DIRECTE DU HTML BRUT
                string contentToSave = htmlContent;

                Debug.WriteLine($"Saving RAW HTML content for offline reading: {item.Link}");

                await _dbService.SaveArticleContentAsync(item, contentToSave);

                // Mettre à jour l'objet local après la sauvegarde
                item.IsDownloaded = true;
                item.ContentHtml = contentToSave;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error downloading full content for {item.Link}: {ex.Message}");
                // IMPORTANT : On relance l'exception pour que le ViewModel puisse afficher l'alerte à l'utilisateur
                throw;
            }
        }

        // --------------------------------------------------------
        // --- LOGIQUE DE TÉLÉCHARGEMENT EN MASSE (Batch) (Inchangée) ---
        // --------------------------------------------------------

        /// <summary>
        /// Télécharge le contenu de tous les articles non téléchargés dans une playlist.
        /// </summary>
        public async Task DownloadAllContentForPlaylistAsync(int playlistId)
        {
            var itemsToDownload = await _dbService.GetItemsToDownloadAsync(playlistId);

            if (!itemsToDownload.Any())
            {
                Debug.WriteLine("No articles to download for offline reading.");
                return;
            }

            const int maxConcurrentDownloads = 4;
            using var semaphore = new SemaphoreSlim(maxConcurrentDownloads);

            var downloadTasks = itemsToDownload.Select(async item =>
            {
                await semaphore.WaitAsync();
                try
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.Link)) return;

                    string htmlContent = await _httpClient.GetStringAsync(item.Link);

                    // SAUVEGARDE DIRECTE DU HTML BRUT (pour le traitement par lots)
                    item.ContentHtml = htmlContent;
                    item.IsDownloaded = true;
                }
                catch (Exception ex)
                {
                    // Marquer comme non téléchargé si échec
                    item.IsDownloaded = false;
                    Debug.WriteLine($"Skipping article {item.Title} due to error: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            await Task.WhenAll(downloadTasks);

            // Sauvegarde de tous les articles mis à jour en une seule transaction
            var successfullyDownloadedItems = itemsToDownload.Where(i => i.IsDownloaded).ToList();

            if (successfullyDownloadedItems.Any())
            {
                // Utilise la méthode d'Update par lots de SQLiteService
                await _dbService.UpdateItemsWithContentAsync(successfullyDownloadedItems);
                Debug.WriteLine($"Successfully downloaded and saved content for {successfullyDownloadedItems.Count} items.");
            }
        }


        // --------------------------------------------------
        // --- MÉTHODES UTILITAIRES (Inchangées) ---
        // --------------------------------------------------

        private string ExtractImageUrlFromHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return null;

            var match = Regex.Match(html, @"<img\s+[^>]*src\s*=\s*['""]([^'""]+)['""][^>]*>", RegexOptions.IgnoreCase);

            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return null;
        }

        private string CleanHtmlSummary(string summary)
        {
            if (string.IsNullOrWhiteSpace(summary)) return summary;

            string cleanText = Regex.Replace(summary, "<[^>]*>", string.Empty);
            cleanText = WebUtility.HtmlDecode(cleanText);

            const int maxInitialLength = 2400;

            if (cleanText.Length > maxInitialLength)
            {
                return cleanText.Substring(0, maxInitialLength).Trim() + "...";
            }

            return cleanText.Trim();
        }
    }
}