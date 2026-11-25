using Rss_feeder_prout.Helpers;
using Rss_feeder_prout.Models;
using SQLite;
using System.Diagnostics;
using System.IO.Compression;

namespace Rss_feeder_prout.Services
{
    public partial class SQLiteService
    {
        // =================================================================
        // ARCHIVES PERMANENTES (RssArchives.db3)
        // =================================================================

        public async Task<int> InsertArchiveAsync(ArchiveItem archive)
        {
            await EnsureInitialized();

            if (!string.IsNullOrEmpty(archive.ArticleGuid))
            {
                var existing = await ArchiveDb.Table<ArchiveItem>()
                                              .Where(x => x.ArticleGuid == archive.ArticleGuid)
                                              .FirstOrDefaultAsync();
                if (existing != null) return 0;
            }

            return await ArchiveDb.InsertAsync(archive);
        }

        /// <summary>Conservés (anciens noms).</summary>
        public Task ArchiveArticleAsync(ArchiveItem item) => InsertArchiveAsync(item);
        public Task AddToArchivesAsync(ArchiveItem item) => InsertArchiveAsync(item);

        public async Task<bool> IsArchivedAsync(string articleGuid)
        {
            if (string.IsNullOrEmpty(articleGuid)) return false;
            await EnsureInitialized();
            return await ArchiveDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Archives WHERE ArticleGuid = ?;", articleGuid) > 0;
        }

        public async Task<List<ArchiveItem>> GetArchivesAsync(string? search = null)
        {
            await EnsureInitialized();

            if (string.IsNullOrWhiteSpace(search))
            {
                return await ArchiveDb.QueryAsync<ArchiveItem>(
                    "SELECT Id, Title, Summary, PublishDate, PublishDateUtc, ArticleGuid, Link, ArchivedAt, Author, ImageUrl, " +
                    "LocalImagePath, SiteId, SiteName, ReadingMinutes, Note FROM Archives ORDER BY ArchivedAt DESC;");
            }

            var like = "%" + search.Trim() + "%";
            return await ArchiveDb.QueryAsync<ArchiveItem>(
                "SELECT Id, Title, Summary, PublishDate, PublishDateUtc, ArticleGuid, Link, ArchivedAt, Author, ImageUrl, " +
                "LocalImagePath, SiteId, SiteName, ReadingMinutes, Note FROM Archives " +
                "WHERE Title LIKE ? OR Summary LIKE ? OR SiteName LIKE ? OR Note LIKE ? ORDER BY ArchivedAt DESC;",
                like, like, like, like);
        }

        /// <summary>Archives complètes (avec contenu) pour l'export.</summary>
        public async Task<List<ArchiveItem>> GetArchivesFullAsync()
        {
            await EnsureInitialized();
            return await ArchiveDb.QueryAsync<ArchiveItem>("SELECT * FROM Archives ORDER BY ArchivedAt DESC;");
        }

        public async Task<ArchiveItem> GetArchiveByIdAsync(int id)
        {
            await EnsureInitialized();
            return await ArchiveDb.Table<ArchiveItem>().FirstOrDefaultAsync(a => a.Id == id);
        }

        public async Task<int> UpdateArchiveAsync(ArchiveItem archive)
        {
            await EnsureInitialized();
            return await ArchiveDb.UpdateAsync(archive);
        }

        public async Task<int> DeleteArchiveAsync(ArchiveItem archive)
        {
            await EnsureInitialized();
            return await ArchiveDb.DeleteAsync(archive);
        }

        /// <summary>Supprime les archives plus vieilles que N mois (CORRIGÉ : visait la mauvaise base).</summary>
        public async Task<int> CleanupArchivesAsync(int months)
        {
            await EnsureInitialized();
            DateTime limitDate = DateTime.Now.AddMonths(-months);
            return await ArchiveDb.Table<ArchiveItem>()
                                  .Where(x => x.ArchivedAt < limitDate)
                                  .DeleteAsync();
        }

        public async Task<int> CountArchivesAsync()
        {
            await EnsureInitialized();
            return await ArchiveDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Archives;");
        }

        // =================================================================
        // ARCHIVES FROIDES (anciens articles déplacés par mois)
        // =================================================================

        /// <summary>
        /// Déplace les articles plus vieux que N jours (hors favoris / plus tard) dans
        /// ColdArchives/Articles_AAAA-MM.db3, puis les compresse en .zip si demandé.
        /// </summary>
        public async Task<(int moved, int files)> MoveOldArticlesToColdArchiveAsync(int olderThanDays, bool compress)
        {
            await EnsureInitialized();
            var limit = DateTime.UtcNow.AddDays(-olderThanDays).Ticks;

            var items = await ArticlesDb.QueryAsync<RssItem>(
                "SELECT * FROM RssItem WHERE PublishDateUtc < ? AND IsFavorite = 0 AND IsReadLater = 0;", limit);
            if (items.Count == 0) return (0, 0);

            int moved = 0;
            int files = 0;
            foreach (var group in items.GroupBy(i => DateTime.SpecifyKind(i.PublishDateUtc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM")))
            {
                var dbPath = Path.Combine(ColdArchiveFolder, $"Articles_{group.Key}.db3");
                var cold = new SQLiteAsyncConnection(dbPath, DbFlags);
                try
                {
                    await cold.CreateTableAsync<RssItem>();
                    var batch = group.ToList();
                    int count = 0;
                    await cold.RunInTransactionAsync(conn =>
                    {
                        foreach (var it in batch)
                        {
                            var copy = it;
                            int originalId = copy.Id;
                            copy.Id = 0;
                            count += conn.Insert(copy, "OR IGNORE");
                            copy.Id = originalId;
                        }
                    });
                    moved += count;
                    files++;
                }
                finally
                {
                    await cold.CloseAsync();
                }

                // Suppression de la base principale (par lots)
                var ids = group.Select(i => i.Id).ToList();
                for (int i = 0; i < ids.Count; i += 500)
                {
                    var chunk = ids.Skip(i).Take(500);
                    await ArticlesDb.ExecuteAsync($"DELETE FROM RssItem WHERE Id IN ({string.Join(",", chunk)});");
                }

                if (compress)
                {
                    CompressColdArchive(dbPath);
                }
            }

            RaiseArticlesChanged();
            return (moved, files);
        }

        private static void CompressColdArchive(string dbPath)
        {
            try
            {
                var zipPath = Path.Combine(ColdArchiveFolder,
                    $"{Path.GetFileNameWithoutExtension(dbPath)}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    zip.CreateEntryFromFile(dbPath, Path.GetFileName(dbPath), CompressionLevel.SmallestSize);
                }
                File.Delete(dbPath);
                foreach (var s in new[] { "-wal", "-shm" }) if (File.Exists(dbPath + s)) File.Delete(dbPath + s);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[COLD] Compression impossible : {ex.Message}");
            }
        }

        public List<DbFileInfo> ListColdArchives()
        {
            var list = new List<DbFileInfo>();
            if (!Directory.Exists(ColdArchiveFolder)) return list;

            foreach (var f in Directory.EnumerateFiles(ColdArchiveFolder)
                         .Where(f => f.EndsWith(".db3", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
            {
                var info = new FileInfo(f);
                list.Add(new DbFileInfo
                {
                    Name = info.Name,
                    FullPath = info.FullName,
                    SizeBytes = info.Length,
                    Modified = info.LastWriteTime,
                    Kind = "cold",
                    Description = info.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ? "Compressée" : "Base SQLite"
                });
            }
            return list.OrderByDescending(f => f.Name).ToList();
        }

        /// <summary>Réintègre une archive froide (.db3 ou .zip) dans la base principale.</summary>
        public async Task<int> RestoreColdArchiveAsync(string path)
        {
            await EnsureInitialized();
            int total = 0;

            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var tmp = Path.Combine(Microsoft.Maui.Storage.FileSystem.CacheDirectory, "cold_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tmp);
                try
                {
                    ZipFile.ExtractToDirectory(path, tmp, true);
                    foreach (var db in Directory.EnumerateFiles(tmp, "*.db3", SearchOption.AllDirectories))
                        total += await MergeArticlesFromDatabaseFileAsync(db);
                }
                finally
                {
                    try { Directory.Delete(tmp, true); } catch { }
                }
            }
            else
            {
                total = await MergeArticlesFromDatabaseFileAsync(path);
            }

            return total;
        }

        /// <summary>
        /// Fusionne la table RssItem d'un fichier .db3 externe (ancienne base quotidienne, archive froide,
        /// export d'une autre installation) dans la base principale, sans doublons.
        /// </summary>
        public async Task<int> MergeArticlesFromDatabaseFileAsync(string path)
        {
            await EnsureArticlesReadyForMergeAsync();

            var src = new SQLiteAsyncConnection(path, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex);
            List<RssItem> items;
            try
            {
                var info = await src.GetTableInfoAsync("RssItem");
                if (info == null || info.Count == 0) return 0;
                items = await src.QueryAsync<RssItem>("SELECT * FROM RssItem;");
            }
            finally
            {
                await src.CloseAsync();
            }

            if (items.Count == 0) return 0;

            var sites = (await ConfigDb.Table<FeedSite>().ToListAsync()).ToDictionary(s => s.Id);
            var fileDate = File.GetLastWriteTimeUtc(path);

            foreach (var item in items)
            {
                item.Id = 0;

                if (item.PublishDateUtc == default)
                    item.PublishDateUtc = DateHelper.ParseFeedDate(item.PublishDate) ?? fileDate;

                if (item.FetchedAtUtc == default)
                    item.FetchedAtUtc = item.PublishDateUtc;

                if (string.IsNullOrWhiteSpace(item.ArticleGuid))
                    item.ArticleGuid = !string.IsNullOrWhiteSpace(item.Link) ? item.Link : FormatHelper.StableHash(item.Title + item.PublishDate);

                if (string.IsNullOrWhiteSpace(item.SiteName) && item.SiteId.HasValue && sites.TryGetValue(item.SiteId.Value, out var site))
                    item.SiteName = site.Name;

                // Anciennes bases : chemin local stocké dans ImageUrl
                if (!string.IsNullOrEmpty(item.ImageUrl) && !item.ImageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrEmpty(item.LocalImagePath))
                {
                    item.LocalImagePath = item.ImageUrl.StartsWith("file://") ? item.ImageUrl.Substring(7) : item.ImageUrl;
                }

                if (!string.IsNullOrWhiteSpace(item.ContentHtml))
                {
                    if (item.ReadingMinutes == 0) item.ReadingMinutes = HtmlHelper.EstimateReadingMinutes(item.ContentHtml);
                    item.IsDownloaded = true;
                }

                if (string.IsNullOrEmpty(item.SearchBlob))
                    item.BuildSearchBlob(string.IsNullOrEmpty(item.ContentHtml) ? null : HtmlHelper.StripHtml(item.ContentHtml));
            }

            int inserted = 0;
            await ArticlesDb.RunInTransactionAsync(conn =>
            {
                foreach (var item in items)
                    inserted += conn.Insert(item, "OR IGNORE");
            });

            RaiseArticlesChanged();
            return inserted;
        }

        /// <summary>Pendant l'init, _isInitialized est encore faux : on n'attend pas le sémaphore.</summary>
        private async Task EnsureArticlesReadyForMergeAsync()
        {
            if (_articlesDb != null && _configDb != null) return;
            await EnsureInitialized();
        }

        public async Task DeleteColdArchiveAsync(string path)
        {
            await Task.Run(() =>
            {
                if (File.Exists(path)) File.Delete(path);
                foreach (var s in new[] { "-wal", "-shm" }) if (File.Exists(path + s)) File.Delete(path + s);
            });
        }
    }
}
