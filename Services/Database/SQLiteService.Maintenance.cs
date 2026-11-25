using Rss_feeder_prout.Helpers;
using Rss_feeder_prout.Models;
using SQLite;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Rss_feeder_prout.Services
{
    public partial class SQLiteService
    {
        // =================================================================
        // COMPACTAGE / OPTIMISATION
        // =================================================================

        /// <summary>
        /// Compacte les 3 bases (VACUUM + ANALYZE). Retourne la taille avant / après en octets.
        /// </summary>
        public async Task<(long before, long after)> VacuumAllAsync()
        {
            await EnsureInitialized();
            long before = CurrentDatabasesSize();

            foreach (var conn in new[] { ConfigDb, ArticlesDb, ArchiveDb })
            {
                try
                {
                    await conn.ExecuteScalarAsync<int>("PRAGMA wal_checkpoint(TRUNCATE);");
                    await conn.ExecuteAsync("VACUUM;");
                    await conn.ExecuteAsync("ANALYZE;");
                    await conn.ExecuteScalarAsync<int>("PRAGMA wal_checkpoint(TRUNCATE);");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DB] VACUUM {conn.DatabasePath}: {ex.Message}");
                }
            }

            long after = CurrentDatabasesSize();
            return (before, after);
        }

        public long CurrentDatabasesSize() =>
            FormatHelper.FileSizeWithWal(ConfigDbPath) + FormatHelper.FileSizeWithWal(ArticlesDbPath) + FormatHelper.FileSizeWithWal(ArchiveDbPath);

        /// <summary>Vérifie l'intégrité des 3 bases.</summary>
        public async Task<Dictionary<string, string>> IntegrityCheckAsync()
        {
            await EnsureInitialized();
            var result = new Dictionary<string, string>();
            foreach (var (name, conn) in new[] { ("Configuration", ConfigDb), ("Articles", ArticlesDb), ("Archives", ArchiveDb) })
            {
                try
                {
                    result[name] = await conn.ExecuteScalarAsync<string>("PRAGMA integrity_check;") ?? "?";
                }
                catch (Exception ex)
                {
                    result[name] = "Erreur : " + ex.Message;
                }
            }
            return result;
        }

        // =================================================================
        // RAPPORT DE STOCKAGE
        // =================================================================

        public async Task<StorageReport> GetStorageReportAsync()
        {
            await EnsureInitialized();

            var r = new StorageReport
            {
                ConfigBytes = FormatHelper.FileSizeWithWal(ConfigDbPath),
                ArticlesBytes = FormatHelper.FileSizeWithWal(ArticlesDbPath),
                ArchivesBytes = FormatHelper.FileSizeWithWal(ArchiveDbPath),
            };

            await Task.Run(() =>
            {
                r.ImageCacheBytes = FormatHelper.DirectorySize(ImageCacheFolder, out var imgCount);
                r.ImageCacheCount = imgCount;

                // Anciennes images (racine AppData, version précédente)
                foreach (var f in Directory.EnumerateFiles(AppDataFolder, "cache_*"))
                {
                    try { r.ImageCacheBytes += new FileInfo(f).Length; r.ImageCacheCount++; } catch { }
                }

                r.IconsBytes = FormatHelper.DirectorySize(IconsFolder, out _);
                r.BackupsBytes = FormatHelper.DirectorySize(BackupsFolder, out var bCount, "*.zip");
                r.BackupCount = bCount;
                r.ColdArchiveBytes = FormatHelper.DirectorySize(ColdArchiveFolder, out var cCount);
                r.ColdArchiveCount = cCount;
                r.LegacyBytes = FormatHelper.DirectorySize(LegacyFolder, out var lCount);
                r.LegacyCount = lCount;
            });

            r.ArticleCount = await ArticlesDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM RssItem;");
            r.DownloadedCount = await ArticlesDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM RssItem WHERE IsDownloaded = 1;");
            r.ArchiveCount = await ArchiveDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Archives;");
            r.SiteCount = await ConfigDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM FeedSite;");
            r.PlaylistCount = await ConfigDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM FeedPlaylist;");
            return r;
        }

        /// <summary>Liste les fichiers de base de données (actifs + anciens).</summary>
        public List<DbFileInfo> ListDatabaseFiles()
        {
            var list = new List<DbFileInfo>();

            void Add(string path, string kind, string desc)
            {
                if (!File.Exists(path)) return;
                var info = new FileInfo(path);
                list.Add(new DbFileInfo
                {
                    Name = info.Name,
                    FullPath = info.FullName,
                    SizeBytes = FormatHelper.FileSizeWithWal(info.FullName),
                    Modified = info.LastWriteTime,
                    Kind = kind,
                    Description = desc
                });
            }

            Add(ConfigDbPath, "active", "Catégories & sources");
            Add(ArticlesDbPath, "active", "Articles");
            Add(ArchiveDbPath, "active", "Archives");

            if (Directory.Exists(LegacyFolder))
            {
                foreach (var f in Directory.EnumerateFiles(LegacyFolder, "*.db3").OrderByDescending(f => f))
                    Add(f, "legacy", "Ancienne base (migrée)");
            }

            // Fichiers isolés éventuels à la racine (anciennes versions)
            foreach (var f in Directory.EnumerateFiles(AppDataFolder, "*.db3"))
                Add(f, "legacy", "Ancien emplacement");

            return list;
        }

        public async Task<int> DeleteLegacyFilesAsync()
        {
            int n = 0;
            await Task.Run(() =>
            {
                if (!Directory.Exists(LegacyFolder)) return;
                foreach (var f in Directory.EnumerateFiles(LegacyFolder).ToList())
                {
                    try { File.Delete(f); n++; } catch (Exception ex) { Debug.WriteLine($"[LEGACY] {f}: {ex.Message}"); }
                }
            });
            return n;
        }

        // =================================================================
        // IMAGES ORPHELINES
        // =================================================================

        /// <summary>Supprime du cache les images qui ne sont plus référencées par aucun article ni archive.</summary>
        public async Task<(int deleted, long freed)> CleanOrphanImagesAsync()
        {
            await EnsureInitialized();
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var regex = new Regex(@"ImageCache[/\\]([0-9a-f]{40}\.[a-z0-9]{2,5})", RegexOptions.IgnoreCase);

            void Collect(string? text)
            {
                if (string.IsNullOrEmpty(text)) return;
                foreach (Match m in regex.Matches(text)) referenced.Add(m.Groups[1].Value);
            }

            // Lecture par lots pour éviter de tout charger en mémoire
            int offset = 0;
            while (true)
            {
                var batch = await ArticlesDb.QueryAsync<RssItem>(
                    "SELECT Id, LocalImagePath, ContentHtml FROM RssItem WHERE ImagesCached = 1 OR LocalImagePath <> '' ORDER BY Id LIMIT 200 OFFSET ?;", offset);
                if (batch.Count == 0) break;
                foreach (var it in batch) { Collect(it.LocalImagePath); Collect(it.ContentHtml); }
                offset += batch.Count;
            }

            var archives = await ArchiveDb.QueryAsync<ArchiveItem>("SELECT Id, LocalImagePath, ContentHtml FROM Archives;");
            foreach (var a in archives) { Collect(a.LocalImagePath); Collect(a.ContentHtml); }

            int deleted = 0;
            long freed = 0;
            await Task.Run(() =>
            {
                if (!Directory.Exists(ImageCacheFolder)) return;
                foreach (var f in Directory.EnumerateFiles(ImageCacheFolder).ToList())
                {
                    if (referenced.Contains(Path.GetFileName(f))) continue;
                    try
                    {
                        var len = new FileInfo(f).Length;
                        File.Delete(f);
                        deleted++;
                        freed += len;
                    }
                    catch { }
                }
            });
            return (deleted, freed);
        }

        // =================================================================
        // IMPORT DE CONFIGURATION (fusion d'un ancien RssConfig.db3)
        // =================================================================

        /// <summary>Ajoute les catégories / sources d'un autre fichier de configuration (sans doublons).</summary>
        public async Task<(int playlists, int sites)> MergeConfigFromDatabaseFileAsync(string path)
        {
            await EnsureInitialized();
            var src = new SQLiteAsyncConnection(path, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex);
            List<FeedPlaylist> playlists;
            List<FeedSite> sites;
            try
            {
                var pInfo = await src.GetTableInfoAsync("FeedPlaylist");
                var sInfo = await src.GetTableInfoAsync("FeedSite");
                if (pInfo.Count == 0 || sInfo.Count == 0) return (0, 0);
                playlists = await src.QueryAsync<FeedPlaylist>("SELECT * FROM FeedPlaylist;");
                sites = await src.QueryAsync<FeedSite>("SELECT * FROM FeedSite;");
            }
            finally
            {
                await src.CloseAsync();
            }

            var existingPlaylists = await ConfigDb.Table<FeedPlaylist>().ToListAsync();
            var existingSites = await ConfigDb.Table<FeedSite>().ToListAsync();
            var urlSet = new HashSet<string>(existingSites.Select(s => s.FeedUrl.TrimEnd('/')), StringComparer.OrdinalIgnoreCase);

            int addedP = 0, addedS = 0;
            foreach (var p in playlists)
            {
                var target = existingPlaylists.FirstOrDefault(e => string.Equals(e.Name, p.Name, StringComparison.CurrentCultureIgnoreCase));
                if (target == null)
                {
                    target = new FeedPlaylist { Name = p.Name, IsActive = true, ColorHex = p.ColorHex };
                    await SavePlaylistAsync(target);
                    existingPlaylists.Add(target);
                    addedP++;
                }

                foreach (var s in sites.Where(s => s.PlaylistId == p.Id))
                {
                    if (string.IsNullOrWhiteSpace(s.FeedUrl) || urlSet.Contains(s.FeedUrl.TrimEnd('/'))) continue;
                    s.Id = 0;
                    s.PlaylistId = target.Id;
                    s.LocalIconPath = string.Empty;
                    await ConfigDb.InsertAsync(s);
                    urlSet.Add(s.FeedUrl.TrimEnd('/'));
                    addedS++;
                }
            }
            return (addedP, addedS);
        }

        /// <summary>Détecte le contenu d'un fichier .db3 externe : "config", "articles", "archives" ou "".</summary>
        public static async Task<string> DetectDatabaseKindAsync(string path)
        {
            var conn = new SQLiteAsyncConnection(path, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex);
            try
            {
                if ((await conn.GetTableInfoAsync("FeedSite")).Count > 0) return "config";
                if ((await conn.GetTableInfoAsync("RssItem")).Count > 0) return "articles";
                if ((await conn.GetTableInfoAsync("Archives")).Count > 0) return "archives";
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
            finally
            {
                await conn.CloseAsync();
            }
        }

        /// <summary>Fusionne les archives d'un autre fichier RssArchives.db3 / Archives.db3.</summary>
        public async Task<int> MergeArchivesFromDatabaseFileAsync(string path)
        {
            await EnsureInitialized();
            var src = new SQLiteAsyncConnection(path, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex);
            List<ArchiveItem> items;
            try
            {
                items = await src.QueryAsync<ArchiveItem>("SELECT * FROM Archives;");
            }
            finally
            {
                await src.CloseAsync();
            }

            int added = 0;
            foreach (var a in items)
            {
                a.Id = 0;
                added += await InsertArchiveAsync(a);
            }
            return added;
        }

        // =================================================================
        // RÉINITIALISATION TOTALE
        // =================================================================

        /// <summary>
        /// Supprime TOUTES les bases (Config, Articles, Archives + anciennes bases).
        /// Elles seront recréées (avec les sources par défaut) au prochain accès.
        /// </summary>
        public async Task DeleteDatabaseFileAsync()
        {
            await CloseAllAsync();

            void DeleteWithWal(string p)
            {
                foreach (var f in new[] { p, p + "-wal", p + "-shm" })
                {
                    try { if (File.Exists(f)) File.Delete(f); } catch (Exception ex) { Debug.WriteLine($"Impossible de supprimer {f} : {ex.Message}"); }
                }
            }

            DeleteWithWal(ConfigDbPath);
            DeleteWithWal(ArticlesDbPath);
            DeleteWithWal(ArchiveDbPath);

            // Anciens fichiers éventuellement restés à la racine
            foreach (var file in Directory.GetFiles(AppDataFolder, "*.db3"))
                DeleteWithWal(file);

            await ReopenAsync();
        }
    }
}
