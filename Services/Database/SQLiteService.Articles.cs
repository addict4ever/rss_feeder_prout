using AngleSharp;
using AngleSharp.Dom;
using Rss_feeder_prout.Helpers;
using Rss_feeder_prout.Models;
using SQLite;
using System.Diagnostics;
using System.Text;

namespace Rss_feeder_prout.Services
{
    public partial class SQLiteService
    {
        /// <summary>Colonnes chargées pour les LISTES (sans le contenu HTML, beaucoup plus rapide).</summary>
        private const string ListColumns =
            "Id, ArticleGuid, Title, Summary, Link, Author, ImageUrl, LocalImagePath, PublishDate, PublishDateUtc, " +
            "FetchedAtUtc, PlaylistId, SiteId, SiteName, IsRead, ReadAtUtc, IsFavorite, IsReadLater, IsDownloaded, " +
            "DownloadedAtUtc, ImagesCached, ReadingMinutes, Categories";

        // =================================================================
        // REQUÊTES
        // =================================================================

        private static string BuildWhere(ArticleQuery q, List<object> args)
        {
            var sb = new StringBuilder(" WHERE 1=1");

            if (q.PlaylistIds != null && q.PlaylistIds.Count > 0)
            {
                sb.Append(" AND PlaylistId IN (").Append(string.Join(",", q.PlaylistIds.Select(i => i.ToString()))).Append(')');
            }
            else if (q.PlaylistId.HasValue)
            {
                sb.Append(" AND PlaylistId = ?");
                args.Add(q.PlaylistId.Value);
            }

            if (q.SiteId.HasValue)
            {
                sb.Append(" AND SiteId = ?");
                args.Add(q.SiteId.Value);
            }

            switch (q.Filter)
            {
                case ArticleFilter.Unread: sb.Append(" AND IsRead = 0"); break;
                case ArticleFilter.Read: sb.Append(" AND IsRead = 1"); break;
                case ArticleFilter.Favorites: sb.Append(" AND IsFavorite = 1"); break;
                case ArticleFilter.ReadLater: sb.Append(" AND IsReadLater = 1"); break;
                case ArticleFilter.Downloaded: sb.Append(" AND IsDownloaded = 1"); break;
                case ArticleFilter.NotDownloaded: sb.Append(" AND IsDownloaded = 0"); break;
            }

            if (q.SinceUtc.HasValue)
            {
                sb.Append(" AND PublishDateUtc >= ?");
                args.Add(q.SinceUtc.Value.Ticks);
            }

            if (!string.IsNullOrWhiteSpace(q.Search))
            {
                if (q.SearchTitleOnly)
                {
                    // LIKE de SQLite ignore la casse (ASCII) : on cherche chaque mot tel quel ET sans accents
                    var rawTerms = q.Search.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var term in rawTerms.Take(8))
                    {
                        var clean = term.Replace("%", "").Replace("_", "");
                        sb.Append(" AND (Title LIKE ? OR Title LIKE ?)");
                        args.Add("%" + clean + "%");
                        args.Add("%" + HtmlHelper.RemoveAccents(clean) + "%");
                    }
                }
                else
                {
                    var terms = HtmlHelper.NormalizeForSearch(q.Search).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var term in terms.Take(8))
                    {
                        sb.Append(" AND SearchBlob LIKE ?");
                        args.Add("%" + term.Replace("%", "").Replace("_", "") + "%");
                    }
                }
            }

            return sb.ToString();
        }

        private static string BuildOrder(ArticleQuery q) => q.Sort switch
        {
            ArticleSort.Oldest => " ORDER BY PublishDateUtc ASC",
            ArticleSort.BySite => " ORDER BY SiteName COLLATE NOCASE ASC, PublishDateUtc DESC",
            _ => " ORDER BY PublishDateUtc DESC"
        };

        /// <summary>Requête principale des listes d'articles (sans contenu HTML).</summary>
        public async Task<List<RssItem>> QueryArticlesAsync(ArticleQuery q)
        {
            await EnsureInitialized();
            var args = new List<object>();
            var sql = $"SELECT {ListColumns} FROM RssItem" + BuildWhere(q, args) + BuildOrder(q) + " LIMIT ? OFFSET ?;";
            args.Add(Math.Max(1, q.Limit));
            args.Add(Math.Max(0, q.Offset));
            return await ArticlesDb.QueryAsync<RssItem>(sql, args.ToArray());
        }

        public async Task<int> CountArticlesAsync(ArticleQuery q)
        {
            await EnsureInitialized();
            var args = new List<object>();
            var sql = "SELECT COUNT(*) FROM RssItem" + BuildWhere(q, args) + ";";
            return await ArticlesDb.ExecuteScalarAsync<int>(sql, args.ToArray());
        }

        /// <summary>Article complet (avec contenu HTML).</summary>
        public async Task<RssItem> GetItemByIdAsync(int id)
        {
            // Utilise le mécanisme sécurisé non-bloquant
            await EnsureInitialized();
            return await ArticlesDb.Table<RssItem>().Where(i => i.Id == id).FirstOrDefaultAsync();
        }

        /// <summary>Conservé (alias).</summary>
        public Task<RssItem> GetRssItemAsync(int id) => GetItemByIdAsync(id);

        public async Task<RssItem?> GetItemByGuidAsync(int? siteId, string guid)
        {
            await EnsureInitialized();
            var list = await ArticlesDb.QueryAsync<RssItem>("SELECT * FROM RssItem WHERE SiteId IS ? AND ArticleGuid = ? LIMIT 1;", siteId!, guid);
            return list.FirstOrDefault();
        }

        /// <summary>Retourne les GUID déjà présents pour une source (anti-doublons très rapide).</summary>
        public async Task<HashSet<string>> GetExistingGuidsAsync(int siteId, IEnumerable<string> guids)
        {
            await EnsureInitialized();
            var result = new HashSet<string>(StringComparer.Ordinal);
            var all = guids.Where(g => !string.IsNullOrEmpty(g)).Distinct().ToList();

            for (int i = 0; i < all.Count; i += 400)
            {
                var chunk = all.Skip(i).Take(400).ToList();
                var placeholders = string.Join(",", chunk.Select(_ => "?"));
                var args = new List<object> { siteId };
                args.AddRange(chunk);
                var rows = await ArticlesDb.QueryAsync<RssItem>($"SELECT ArticleGuid FROM RssItem WHERE SiteId = ? AND ArticleGuid IN ({placeholders});", args.ToArray());
                foreach (var r in rows) result.Add(r.ArticleGuid);
            }
            return result;
        }

        /// <summary>Insère les nouveaux articles en ignorant les doublons. Retourne le nombre inséré.</summary>
        public async Task<int> InsertNewItemsAsync(IEnumerable<RssItem> items)
        {
            await EnsureInitialized();
            var list = items.ToList();
            if (list.Count == 0) return 0;

            int inserted = 0;
            await ArticlesDb.RunInTransactionAsync(conn =>
            {
                foreach (var item in list)
                {
                    item.Id = 0;
                    inserted += conn.Insert(item, "OR IGNORE");
                }
            });

            if (inserted > 0) RaiseArticlesChanged();
            return inserted;
        }

        /// <summary>Enregistre un article "en ligne" et retourne la version stockée (avec Id).</summary>
        public async Task<RssItem?> SaveLiveItemAsync(RssItem item)
        {
            await EnsureInitialized();
            if (item.FetchedAtUtc == default) item.FetchedAtUtc = DateTime.UtcNow;
            if (string.IsNullOrEmpty(item.SearchBlob)) item.BuildSearchBlob();

            item.Id = 0;
            await ArticlesDb.InsertAsync(item, "OR IGNORE");
            var stored = await GetItemByGuidAsync(item.SiteId, item.ArticleGuid);
            if (stored != null)
            {
                item.Id = stored.Id;
                item.IsLive = false;
            }
            RaiseArticlesChanged();
            return stored;
        }

        /// <summary>Conservé : insertion / mise à jour en masse.</summary>
        public async Task SaveItemsAsync(IEnumerable<RssItem> items)
        {
            await EnsureInitialized();
            await ArticlesDb.RunInTransactionAsync(conn =>
            {
                foreach (var item in items)
                {
                    if (item.Id != 0)
                        conn.Update(item);
                    else
                        conn.Insert(item, "OR IGNORE");
                }
            });
            RaiseArticlesChanged();
        }

        public async Task<int> UpdateRssItemAsync(RssItem item)
        {
            await EnsureInitialized();
            return await ArticlesDb.UpdateAsync(item);
        }

        public async Task UpdateItemsWithContentAsync(IEnumerable<RssItem> items)
        {
            await EnsureInitialized();
            foreach (var item in items)
            {
                await SaveArticleContentAsync(item, item.ContentHtml);
            }
        }

        /// <summary>
        /// Enregistre le contenu complet (mise à jour CIBLÉE : ne touche pas à lu/favori modifiés entre-temps).
        /// </summary>
        public async Task SaveArticleContentAsync(RssItem item, string fullHtmlContent, bool imagesCached = false, string? localLeadImage = null)
        {
            if (item == null || item.Id == 0) return;
            await EnsureInitialized();

            item.ContentHtml = fullHtmlContent ?? string.Empty;
            item.IsDownloaded = !string.IsNullOrWhiteSpace(fullHtmlContent);
            item.ImagesCached = imagesCached;
            item.DownloadedAtUtc = DateTime.UtcNow;
            item.ReadingMinutes = HtmlHelper.EstimateReadingMinutes(fullHtmlContent);
            if (!string.IsNullOrEmpty(localLeadImage)) item.LocalImagePath = localLeadImage;
            item.BuildSearchBlob(HtmlHelper.StripHtml(fullHtmlContent));

            await ArticlesDb.ExecuteAsync(
                "UPDATE RssItem SET ContentHtml = ?, IsDownloaded = ?, ImagesCached = ?, DownloadedAtUtc = ?, " +
                "ReadingMinutes = ?, SearchBlob = ?, LocalImagePath = ? WHERE Id = ?;",
                item.ContentHtml, item.IsDownloaded, item.ImagesCached, item.DownloadedAtUtc!.Value.Ticks,
                item.ReadingMinutes, item.SearchBlob, item.LocalImagePath ?? string.Empty, item.Id);
        }

        /// <summary>Retire le contenu hors-ligne (libère de la place) sans supprimer l'article.</summary>
        public async Task ClearArticleContentAsync(RssItem item)
        {
            await EnsureInitialized();
            item.ContentHtml = string.Empty;
            item.IsDownloaded = false;
            item.ImagesCached = false;
            await ArticlesDb.ExecuteAsync("UPDATE RssItem SET ContentHtml = '', IsDownloaded = 0, ImagesCached = 0 WHERE Id = ?;", item.Id);
        }

        // =================================================================
        // ÉTATS : lu / favori / plus tard
        // =================================================================

        public async Task MarkItemAsReadAsync(RssItem item)
        {
            if (item == null) return;
            await SetReadAsync(item, true);
        }

        public async Task SetReadAsync(RssItem item, bool isRead)
        {
            if (item == null) return;
            item.IsRead = isRead;
            item.ReadAtUtc = isRead ? DateTime.UtcNow : null;
            if (item.Id == 0) return;

            await EnsureInitialized();
            if (isRead)
                await ArticlesDb.ExecuteAsync("UPDATE RssItem SET IsRead = 1, ReadAtUtc = ? WHERE Id = ?;", DateTime.UtcNow.Ticks, item.Id);
            else
                await ArticlesDb.ExecuteAsync("UPDATE RssItem SET IsRead = 0, ReadAtUtc = NULL WHERE Id = ?;", item.Id);
            RaiseArticlesChanged();
        }

        public async Task SetFavoriteAsync(RssItem item, bool value)
        {
            item.IsFavorite = value;
            if (item.Id == 0) return;
            await EnsureInitialized();
            await ArticlesDb.ExecuteAsync("UPDATE RssItem SET IsFavorite = ? WHERE Id = ?;", value, item.Id);
            RaiseArticlesChanged();
        }

        public async Task SetReadLaterAsync(RssItem item, bool value)
        {
            item.IsReadLater = value;
            if (item.Id == 0) return;
            await EnsureInitialized();
            await ArticlesDb.ExecuteAsync("UPDATE RssItem SET IsReadLater = ? WHERE Id = ?;", value, item.Id);
            RaiseArticlesChanged();
        }

        /// <summary>Marque comme lus tous les articles correspondant à la requête (filtres compris).</summary>
        public async Task<int> MarkAllReadAsync(ArticleQuery scope)
        {
            await EnsureInitialized();
            var args = new List<object> { DateTime.UtcNow.Ticks };
            var sql = "UPDATE RssItem SET IsRead = 1, ReadAtUtc = ?" + BuildWhere(scope, args) + " AND IsRead = 0;";
            int n = await ArticlesDb.ExecuteAsync(sql, args.ToArray());
            RaiseArticlesChanged();
            return n;
        }

        public async Task<int> MarkOlderThanReadAsync(int days)
        {
            await EnsureInitialized();
            var limit = DateTime.UtcNow.AddDays(-days).Ticks;
            int n = await ArticlesDb.ExecuteAsync("UPDATE RssItem SET IsRead = 1, ReadAtUtc = ? WHERE IsRead = 0 AND PublishDateUtc < ?;", DateTime.UtcNow.Ticks, limit);
            RaiseArticlesChanged();
            return n;
        }

        // =================================================================
        // COMPTEURS
        // =================================================================

        public async Task<Dictionary<int, int>> GetUnreadCountsByPlaylistAsync()
        {
            await EnsureInitialized();
            var rows = await ArticlesDb.QueryAsync<CountRow>("SELECT PlaylistId AS K, COUNT(*) AS V FROM RssItem WHERE IsRead = 0 GROUP BY PlaylistId;");
            return rows.ToDictionary(r => r.K, r => r.V);
        }

        public async Task<Dictionary<int, int>> GetUnreadCountsBySiteAsync(int? playlistId = null)
        {
            await EnsureInitialized();
            List<CountRow> rows = playlistId.HasValue
                ? await ArticlesDb.QueryAsync<CountRow>("SELECT IFNULL(SiteId, 0) AS K, COUNT(*) AS V FROM RssItem WHERE IsRead = 0 AND PlaylistId = ? GROUP BY SiteId;", playlistId.Value)
                : await ArticlesDb.QueryAsync<CountRow>("SELECT IFNULL(SiteId, 0) AS K, COUNT(*) AS V FROM RssItem WHERE IsRead = 0 GROUP BY SiteId;");
            return rows.GroupBy(r => r.K).ToDictionary(g => g.Key, g => g.Sum(r => r.V));
        }

        public async Task<Dictionary<int, int>> GetTotalCountsBySiteAsync()
        {
            await EnsureInitialized();
            var rows = await ArticlesDb.QueryAsync<CountRow>("SELECT IFNULL(SiteId, 0) AS K, COUNT(*) AS V FROM RssItem GROUP BY SiteId;");
            return rows.GroupBy(r => r.K).ToDictionary(g => g.Key, g => g.Sum(r => r.V));
        }

        public async Task<Dictionary<int, int>> GetTotalCountsByPlaylistAsync()
        {
            await EnsureInitialized();
            var rows = await ArticlesDb.QueryAsync<CountRow>("SELECT PlaylistId AS K, COUNT(*) AS V FROM RssItem GROUP BY PlaylistId;");
            return rows.ToDictionary(r => r.K, r => r.V);
        }

        public async Task<SmartCounts> GetSmartCountsAsync()
        {
            await EnsureInitialized();
            var todayUtc = DateTime.Today.ToUniversalTime().Ticks;
            var db = ArticlesDb;
            return new SmartCounts
            {
                All = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM RssItem;"),
                Unread = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM RssItem WHERE IsRead = 0;"),
                Today = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM RssItem WHERE PublishDateUtc >= ?;", todayUtc),
                Favorites = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM RssItem WHERE IsFavorite = 1;"),
                ReadLater = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM RssItem WHERE IsReadLater = 1;"),
                Downloaded = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM RssItem WHERE IsDownloaded = 1;")
            };
        }

        public async Task<List<NamedCount>> GetTopSitesAsync(int limit = 10)
        {
            await EnsureInitialized();
            var rows = await ArticlesDb.QueryAsync<NamedCountRow>(
                "SELECT IFNULL(NULLIF(SiteName, ''), '(inconnu)') AS N, COUNT(*) AS V FROM RssItem GROUP BY SiteName ORDER BY V DESC LIMIT ?;", limit);
            int max = rows.Count > 0 ? Math.Max(1, rows.Max(r => r.V)) : 1;
            return rows.Select(r => new NamedCount { Name = r.N, Count = r.V, Ratio = (double)r.V / max }).ToList();
        }

        /// <summary>Articles publiés par jour sur les N derniers jours (statistiques).</summary>
        public async Task<List<NamedCount>> GetArticlesPerDayAsync(int days = 7)
        {
            await EnsureInitialized();
            var result = new List<NamedCount>();
            var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
            for (int i = days - 1; i >= 0; i--)
            {
                var dayStart = DateTime.Today.AddDays(-i);
                var from = dayStart.ToUniversalTime().Ticks;
                var to = dayStart.AddDays(1).ToUniversalTime().Ticks;
                int c = await ArticlesDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM RssItem WHERE PublishDateUtc >= ? AND PublishDateUtc < ?;", from, to);
                result.Add(new NamedCount { Name = dayStart.ToString("ddd d", fr), Count = c });
            }
            int max = result.Count > 0 ? Math.Max(1, result.Max(r => r.Count)) : 1;
            foreach (var r in result) r.Ratio = (double)r.Count / max;
            return result;
        }

        // =================================================================
        // TÉLÉCHARGEMENTS
        // =================================================================

        public async Task<List<RssItem>> GetItemsToDownloadAsync(int playlistId)
        {
            return await GetItemsToDownloadAsync(new[] { playlistId }, 1000);
        }

        /// <summary>Articles à télécharger (les plus récents d'abord, non lus en priorité).</summary>
        public async Task<List<RssItem>> GetItemsToDownloadAsync(IEnumerable<int> playlistIds, int max)
        {
            await EnsureInitialized();
            var ids = playlistIds.ToList();
            if (ids.Count == 0) return new List<RssItem>();
            var sql = $"SELECT {ListColumns} FROM RssItem WHERE IsDownloaded = 0 AND PlaylistId IN ({string.Join(",", ids)}) " +
                      "AND Link <> '' ORDER BY IsRead ASC, PublishDateUtc DESC LIMIT ?;";
            return await ArticlesDb.QueryAsync<RssItem>(sql, max);
        }

        public async Task<List<string>> GetCachedArticleGuidsAsync(int playlistId)
        {
            await EnsureInitialized();
            var rows = await ArticlesDb.QueryAsync<RssItem>("SELECT ArticleGuid FROM RssItem WHERE PlaylistId = ?;", playlistId);
            return rows.Select(r => r.ArticleGuid).ToList();
        }

        // =================================================================
        // MÉTHODES D'ORIGINE (conservées, adaptées à la base unique)
        // =================================================================

        public async Task<bool> IsArticleAlreadyDownloadedAsync(string articleGuid)
        {
            await EnsureInitialized();
            return await ArticlesDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM RssItem WHERE ArticleGuid = ?;", articleGuid) > 0;
        }

        /// <summary>Ancien index : l'unicité est maintenant garantie par l'index unique. Conservé sans effet.</summary>
        public Task AddToArticleIndexAsync(string articleGuid) => Task.CompletedTask;

        /// <summary>Vérifie si l'article existe dans la base principale OU dans une archive froide.</summary>
        public async Task<bool> ItemExistsInAnyDatabaseAsync(string articleGuid)
        {
            if (string.IsNullOrEmpty(articleGuid)) return false;
            await EnsureInitialized();

            int count = await ArticlesDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM RssItem WHERE ArticleGuid = ? OR Link = ?;", articleGuid, articleGuid);
            if (count > 0) return true;

            foreach (var file in Directory.EnumerateFiles(ColdArchiveFolder, "*.db3"))
            {
                var temp = new SQLiteAsyncConnection(file, SQLiteOpenFlags.ReadOnly);
                try
                {
                    if (await temp.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM RssItem WHERE ArticleGuid = ? OR Link = ?;", articleGuid, articleGuid) > 0)
                        return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erreur lecture fichier {Path.GetFileName(file)}: {ex.Message}");
                }
                finally
                {
                    await temp.CloseAsync();
                }
            }
            return false;
        }

        public async Task<List<RssItem>> GetItemsForPlaylistAsync(int playlistId, int? siteId = null)
        {
            return await QueryArticlesAsync(new ArticleQuery { PlaylistId = playlistId, SiteId = siteId, Limit = 2000 });
        }

        public async Task<List<RssItem>> GetAllArticlesFromAllDatabasesAsync()
        {
            return await QueryArticlesAsync(new ArticleQuery { Limit = 100000 });
        }

        // =================================================================
        // NETTOYAGE
        // =================================================================

        public async Task<int> DeleteReadItemsAsync(bool keepSaved = true)
        {
            await EnsureInitialized();
            var sql = "DELETE FROM RssItem WHERE IsRead = 1" + (keepSaved ? " AND IsFavorite = 0 AND IsReadLater = 0;" : ";");
            int n = await ArticlesDb.ExecuteAsync(sql);
            RaiseArticlesChanged();
            return n;
        }

        public async Task<int> CleanupItemsOlderThanAsync(DateTime dateLimit, bool onlyRead = false, bool keepSaved = true)
        {
            await EnsureInitialized();
            var limitUtc = (dateLimit.Kind == DateTimeKind.Utc ? dateLimit : dateLimit.ToUniversalTime()).Ticks;
            var sql = "DELETE FROM RssItem WHERE PublishDateUtc < ?";
            if (onlyRead) sql += " AND IsRead = 1";
            if (keepSaved) sql += " AND IsFavorite = 0 AND IsReadLater = 0";
            int n = await ArticlesDb.ExecuteAsync(sql + ";", limitUtc);
            RaiseArticlesChanged();
            return n;
        }

        public Task<int> CleanupByDaysAsync(int days) => CleanupItemsOlderThanAsync(DateTime.UtcNow.AddDays(-days));

        public Task<int> CleanupByTimeAsync(int value, string unit)
        {
            DateTime limitDate = unit.ToLower() switch
            {
                "mois" => DateTime.UtcNow.AddMonths(-value),
                _ => DateTime.UtcNow.AddDays(-value)
            };
            return CleanupItemsOlderThanAsync(limitDate);
        }

        /// <summary>Nettoyage automatique selon les réglages de rétention. Retourne le nombre supprimé.</summary>
        public async Task<int> ApplyRetentionAsync()
        {
            int total = 0;
            if (AppSettings.RetentionReadDays > 0)
            {
                await EnsureInitialized();
                var limit = DateTime.UtcNow.AddDays(-AppSettings.RetentionReadDays).Ticks;
                total += await ArticlesDb.ExecuteAsync(
                    "DELETE FROM RssItem WHERE IsRead = 1 AND IsFavorite = 0 AND IsReadLater = 0 AND IFNULL(ReadAtUtc, PublishDateUtc) < ?;", limit);
            }
            if (AppSettings.RetentionAllDays > 0)
            {
                total += await CleanupItemsOlderThanAsync(DateTime.UtcNow.AddDays(-AppSettings.RetentionAllDays));
            }
            if (total > 0) RaiseArticlesChanged();
            return total;
        }

        /// <summary>Vide le cache d'articles.</summary>
        public async Task<int> ClearAllCacheAsync(bool keepSaved = false)
        {
            await EnsureInitialized();
            int n = keepSaved
                ? await ArticlesDb.ExecuteAsync("DELETE FROM RssItem WHERE IsFavorite = 0 AND IsReadLater = 0;")
                : await ArticlesDb.ExecuteAsync("DELETE FROM RssItem;");
            RaiseArticlesChanged();
            return n;
        }

        public async Task<int> ClearPlaylistCacheAsync(int playlistId)
        {
            await EnsureInitialized();
            int n = await ArticlesDb.ExecuteAsync("DELETE FROM RssItem WHERE PlaylistId = ? AND IsFavorite = 0 AND IsReadLater = 0;", playlistId);
            RaiseArticlesChanged();
            return n;
        }

        /// <summary>Vide complètement une table connue (sécurisé : liste blanche).</summary>
        public async Task<int> ClearTableAsync(string tableName)
        {
            await EnsureInitialized();
            return tableName switch
            {
                "RssItem" => await ArticlesDb.ExecuteAsync("DELETE FROM RssItem;"),
                "Archives" => await ArchiveDb.ExecuteAsync("DELETE FROM Archives;"),
                "SyncLogEntry" => await ConfigDb.ExecuteAsync("DELETE FROM SyncLogEntry;"),
                _ => 0
            };
        }

        /// <summary>Conservé : nettoie le HTML de tous les articles téléchargés (AngleSharp).</summary>
        public async Task CleanAllContentInDatabaseAsync()
        {
            await EnsureInitialized();
            var items = await ArticlesDb.QueryAsync<RssItem>("SELECT * FROM RssItem WHERE IsDownloaded = 1;");
            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(item.ContentHtml)) continue;
                string cleanedHtml = await CleanHtmlContent(item.ContentHtml);
                await ArticlesDb.ExecuteAsync("UPDATE RssItem SET ContentHtml = ? WHERE Id = ?;", cleanedHtml, item.Id);
            }
        }

        public async Task<string> CleanHtmlContent(string htmlContent)
        {
            if (string.IsNullOrWhiteSpace(htmlContent)) return htmlContent;

            // 1. Initialisation d'AngleSharp
            var config = AngleSharp.Configuration.Default;
            using var context = AngleSharp.BrowsingContext.New(config);
            using var document = await context.OpenAsync(req => req.Content(htmlContent));

            // 2. Liste des éléments parasites à supprimer
            var parasites = new[] {
                "header", "footer", "nav", "aside", "form",
                ".cm-header", ".cm-top-bar", ".breadcrumb-itc", ".bloc-partage",
                ".related-posts-wrapper", ".thumbnail-pagination", ".cm-comments-link",
                ".aioseo-author-bio-compact", ".search-form",
                "[class*='menu']", "[class*='widget']", "[class*='sidebar']",
                "[class*='ad-']", "[class*='ads']",
                "iframe", "script", "style", "noscript",
                ".cm-featured-image", ".cm-entry-header-meta", ".cm-below-entry-meta"
            };

            // 3. Isoler le contenu principal
            var mainContent = document.QuerySelector("article")
                              ?? document.QuerySelector(".cm-post-content")
                              ?? document.QuerySelector(".cm-entry-content")
                              ?? document.Body;
            if (mainContent == null) return htmlContent;

            // 4. Suppression des éléments parasites
            foreach (var selector in parasites)
            {
                foreach (var el in mainContent.QuerySelectorAll(selector).ToList()) el.Remove();
            }

            // 5. Nettoyage des attributs pour alléger le HTML (on garde data-orig-src pour le hors-ligne)
            foreach (var el in mainContent.QuerySelectorAll("*").ToList())
            {
                el.RemoveAttribute("style");
                el.RemoveAttribute("class");
                el.RemoveAttribute("id");
            }

            // 6. Suppression des conteneurs vides (garder les images)
            foreach (var el in mainContent.QuerySelectorAll("p, div, span").ToList())
            {
                if (string.IsNullOrWhiteSpace(el.TextContent) && !el.QuerySelectorAll("img").Any())
                {
                    el.Remove();
                }
            }

            return mainContent.InnerHtml;
        }
    }
}
