using Rss_feeder_prout.Helpers;
using Rss_feeder_prout.Models;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Devices;

namespace Rss_feeder_prout.Services
{
    /// <summary>
    /// Sauvegardes (.zip) / restauration / import / exports.
    ///
    /// Une sauvegarde contient :
    ///   manifest.json, settings.json, RssConfig.db3, RssArticles.db3, RssArchives.db3,
    ///   (optionnel) ImageCache/ et Icons/
    /// Les bases sont copiées avec VACUUM INTO : copie cohérente ET compactée (même en mode WAL).
    /// </summary>
    public class BackupService
    {
        private readonly SQLiteService _db;
        private readonly OpmlService _opml;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public BackupService(SQLiteService db, OpmlService opml)
        {
            _db = db;
            _opml = opml;
        }

        // =================================================================
        // CRÉATION
        // =================================================================

        public async Task<string> CreateBackupAsync(BackupOptions options, IProgress<string>? progress = null)
        {
            await _db.EnsureInitialized();
            Directory.CreateDirectory(SQLiteService.BackupsFolder);

            var prefix = options.IsSafety ? "AvantRestauration" : (options.IsAuto ? "Auto" : "Sauvegarde");
            var zipPath = Path.Combine(SQLiteService.BackupsFolder, $"RssProut_{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
            var tmp = Path.Combine(FileSystem.CacheDirectory, "backup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);

            try
            {
                progress?.Report("Copie de la configuration…");
                await _db.SnapshotAsync("config", Path.Combine(tmp, "RssConfig.db3"));

                if (options.IncludeArticles)
                {
                    progress?.Report("Copie des articles…");
                    await _db.SnapshotAsync("articles", Path.Combine(tmp, "RssArticles.db3"));
                }

                if (options.IncludeArchives)
                {
                    progress?.Report("Copie des archives…");
                    await _db.SnapshotAsync("archives", Path.Combine(tmp, "RssArchives.db3"));
                }

                var report = await _db.GetStorageReportAsync();
                var manifest = new Dictionary<string, object>
                {
                    ["app"] = "Rss_feeder_prout",
                    ["format"] = 2,
                    ["createdUtc"] = DateTime.UtcNow.ToString("o"),
                    ["appVersion"] = AppInfo.Current.VersionString,
                    ["device"] = DeviceInfo.Current.Model,
                    ["articles"] = options.IncludeArticles ? report.ArticleCount : 0,
                    ["archives"] = options.IncludeArchives ? report.ArchiveCount : 0,
                    ["sites"] = report.SiteCount,
                    ["playlists"] = report.PlaylistCount,
                    ["images"] = options.IncludeImages,
                    ["auto"] = options.IsAuto
                };
                await File.WriteAllTextAsync(Path.Combine(tmp, "manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions));

                if (options.IncludeSettings)
                    await File.WriteAllTextAsync(Path.Combine(tmp, "settings.json"), JsonSerializer.Serialize(AppSettings.Export(), JsonOptions));

                progress?.Report("Compression…");
                await Task.Run(() =>
                {
                    if (File.Exists(zipPath)) File.Delete(zipPath);
                    using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                    foreach (var file in Directory.EnumerateFiles(tmp))
                        zip.CreateEntryFromFile(file, Path.GetFileName(file), options.Level);

                    if (options.IncludeImages)
                    {
                        AddFolder(zip, SQLiteService.ImageCacheFolder, "ImageCache", CompressionLevel.Fastest); // déjà compressées
                        AddFolder(zip, SQLiteService.IconsFolder, "Icons", CompressionLevel.Fastest);
                    }
                });

                if (options.IsAuto) PruneAutoBackups();
                return zipPath;
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
            }
        }

        private static void AddFolder(ZipArchive zip, string folder, string entryPrefix, CompressionLevel level)
        {
            if (!Directory.Exists(folder)) return;
            foreach (var f in Directory.EnumerateFiles(folder))
            {
                try { zip.CreateEntryFromFile(f, $"{entryPrefix}/{Path.GetFileName(f)}", level); }
                catch (Exception ex) { Debug.WriteLine($"[BACKUP] {f}: {ex.Message}"); }
            }
        }

        private static void PruneAutoBackups()
        {
            try
            {
                var autos = Directory.EnumerateFiles(SQLiteService.BackupsFolder, "RssProut_Auto_*.zip")
                                     .OrderByDescending(f => f).Skip(AppSettings.BackupKeepCount).ToList();
                foreach (var f in autos) File.Delete(f);
            }
            catch { }
        }

        /// <summary>Sauvegarde automatique si la dernière date de plus de N jours.</summary>
        public async Task RunAutoBackupIfDueAsync()
        {
            if (!AppSettings.AutoBackupEnabled) return;
            var last = AppSettings.LastAutoBackupUtc;
            if (last != DateTime.MinValue && DateTime.UtcNow - DateTime.SpecifyKind(last, DateTimeKind.Utc) < TimeSpan.FromDays(AppSettings.AutoBackupDays))
                return;

            try
            {
                await CreateBackupAsync(new BackupOptions { IsAuto = true, IncludeImages = false, Level = CompressionLevel.Optimal });
                AppSettings.LastAutoBackupUtc = DateTime.UtcNow;
                Debug.WriteLine("[BACKUP] Sauvegarde automatique créée.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BACKUP] Auto : {ex.Message}");
            }
        }

        // =================================================================
        // LISTE
        // =================================================================

        public List<BackupInfo> ListBackups()
        {
            var list = new List<BackupInfo>();
            if (!Directory.Exists(SQLiteService.BackupsFolder)) return list;

            foreach (var f in Directory.EnumerateFiles(SQLiteService.BackupsFolder, "*.zip"))
            {
                var info = new FileInfo(f);
                list.Add(new BackupInfo
                {
                    FileName = info.Name,
                    FullPath = info.FullName,
                    SizeBytes = info.Length,
                    CreatedLocal = info.LastWriteTime,
                    IsAuto = info.Name.Contains("_Auto_"),
                    IsSafety = info.Name.Contains("_AvantRestauration_")
                });
            }
            return list.OrderByDescending(b => b.CreatedLocal).ToList();
        }

        public void DeleteBackup(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        /// <summary>Décrit le contenu d'une sauvegarde (pour confirmation avant restauration).</summary>
        public string Describe(string zipPath)
        {
            try
            {
                using var zip = ZipFile.OpenRead(zipPath);
                var names = zip.Entries.Select(e => e.FullName).ToList();
                var parts = new List<string>();
                if (names.Any(n => n.EndsWith("RssConfig.db3", StringComparison.OrdinalIgnoreCase))) parts.Add("catégories & sources");
                if (names.Any(n => n.EndsWith("RssArticles.db3", StringComparison.OrdinalIgnoreCase) || n.StartsWith("RssProut_", StringComparison.OrdinalIgnoreCase))) parts.Add("articles");
                if (names.Any(n => n.EndsWith("Archives.db3", StringComparison.OrdinalIgnoreCase))) parts.Add("archives");
                if (names.Any(n => n.StartsWith("ImageCache/"))) parts.Add($"{names.Count(n => n.StartsWith("ImageCache/"))} images");
                if (names.Any(n => n == "settings.json")) parts.Add("réglages");

                var manifestEntry = zip.GetEntry("manifest.json");
                if (manifestEntry != null)
                {
                    using var s = manifestEntry.Open();
                    using var doc = JsonDocument.Parse(s);
                    if (doc.RootElement.TryGetProperty("articles", out var a) && a.ValueKind == JsonValueKind.Number)
                        parts.Add($"{a.GetInt32()} articles au total");
                }
                return parts.Count == 0 ? "Contenu non reconnu" : string.Join(", ", parts);
            }
            catch (Exception ex)
            {
                return "Archive illisible : " + ex.Message;
            }
        }

        // =================================================================
        // RESTAURATION
        // =================================================================

        /// <summary>
        /// Restaure une sauvegarde .zip (nouveau format ET ancien "Backup_Total_*.zip").
        /// Une sauvegarde de sécurité est créée automatiquement avant.
        /// </summary>
        public async Task<string> RestoreBackupAsync(string zipPath, RestoreOptions options, IProgress<string>? progress = null)
        {
            progress?.Report("Sauvegarde de sécurité…");
            try
            {
                await CreateBackupAsync(new BackupOptions { IsSafety = true, IncludeImages = false, Level = CompressionLevel.Fastest });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RESTORE] Sauvegarde de sécurité impossible : {ex.Message}");
            }

            var tmp = Path.Combine(FileSystem.CacheDirectory, "restore_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            var summary = new List<string>();

            try
            {
                progress?.Report("Décompression…");
                await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, tmp, true));

                string? Find(string name) => Directory.EnumerateFiles(tmp, name, SearchOption.AllDirectories).FirstOrDefault();

                var config = Find("RssConfig.db3");
                var articles = Find("RssArticles.db3");
                var archives = Find("RssArchives.db3") ?? Find("Archives.db3");
                var legacyDaily = Directory.EnumerateFiles(tmp, "RssProut_*.db3", SearchOption.AllDirectories).ToList();

                if (config == null && articles == null && archives == null && legacyDaily.Count == 0)
                    throw new InvalidOperationException("Cette archive ne contient aucune base Rss Prout.");

                progress?.Report("Remplacement des bases…");
                await _db.CloseAllAsync();

                if (options.RestoreConfig && config != null)
                {
                    ReplaceFile(config, _db.ConfigDbPath);
                    summary.Add("configuration");
                }

                if (options.RestoreArticles && articles != null && !options.MergeArticles)
                {
                    ReplaceFile(articles, _db.ArticlesDbPath);
                    summary.Add("articles");
                }

                if (options.RestoreArchives && archives != null)
                {
                    ReplaceFile(archives, _db.ArchiveDbPath);
                    summary.Add("archives");
                }

                await _db.ReopenAsync();

                // Fusion (articles en mode fusion + anciennes bases quotidiennes)
                if (options.RestoreArticles && articles != null && options.MergeArticles)
                {
                    progress?.Report("Fusion des articles…");
                    int n = await _db.MergeArticlesFromDatabaseFileAsync(articles);
                    summary.Add($"{n} articles fusionnés");
                }

                if (options.RestoreArticles && legacyDaily.Count > 0)
                {
                    progress?.Report("Import des anciennes bases quotidiennes…");
                    int n = 0;
                    foreach (var f in legacyDaily) n += await _db.MergeArticlesFromDatabaseFileAsync(f);
                    summary.Add($"{n} articles (ancien format)");
                }

                if (options.RestoreImages)
                {
                    progress?.Report("Images…");
                    int imgs = await Task.Run(() => CopyFolder(Path.Combine(tmp, "ImageCache"), SQLiteService.ImageCacheFolder)
                                                     + CopyFolder(Path.Combine(tmp, "Icons"), SQLiteService.IconsFolder));
                    if (imgs > 0) summary.Add($"{imgs} images");
                }

                if (options.RestoreSettings)
                {
                    var settings = Find("settings.json");
                    if (settings != null)
                    {
                        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(settings));
                        if (dict != null)
                        {
                            AppSettings.Import(dict);
                            summary.Add("réglages");
                        }
                    }
                }
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
                // Toujours rouvrir, même en cas d'erreur
                await _db.EnsureInitialized();
            }

            return summary.Count == 0 ? "Rien n'a été restauré." : "Restauré : " + string.Join(", ", summary) + ".";
        }

        private static void ReplaceFile(string source, string target)
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                if (File.Exists(target + suffix)) File.Delete(target + suffix);
            }
            File.Copy(source, target, true);
        }

        private static int CopyFolder(string source, string target)
        {
            if (!Directory.Exists(source)) return 0;
            Directory.CreateDirectory(target);
            int n = 0;
            foreach (var f in Directory.EnumerateFiles(source))
            {
                try { File.Copy(f, Path.Combine(target, Path.GetFileName(f)), true); n++; } catch { }
            }
            return n;
        }

        // =================================================================
        // IMPORT D'UN FICHIER EXTERNE
        // =================================================================

        /// <summary>
        /// Importe un fichier choisi par l'utilisateur :
        ///  .zip  -> copié dans Sauvegardes (puis restauration proposée)
        ///  .db3  -> détection automatique (configuration / articles / archives) et FUSION sans doublons
        ///  .opml / .xml -> import de flux
        /// Retourne (type, message, cheminCopié).
        /// </summary>
        public async Task<(string kind, string message, string path)> ImportExternalFileAsync(FileResult file)
        {
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var tmp = Path.Combine(FileSystem.CacheDirectory, "import_" + Guid.NewGuid().ToString("N") + ext);

            using (var stream = await file.OpenReadAsync())
            using (var output = File.Create(tmp))
            {
                await stream.CopyToAsync(output);
            }

            try
            {
                if (ext == ".zip")
                {
                    Directory.CreateDirectory(SQLiteService.BackupsFolder);
                    var dest = Path.Combine(SQLiteService.BackupsFolder, Path.GetFileName(file.FileName));
                    if (File.Exists(dest)) dest = Path.Combine(SQLiteService.BackupsFolder, $"Import_{DateTime.Now:yyyyMMdd_HHmmss}_{Path.GetFileName(file.FileName)}");
                    File.Copy(tmp, dest, true);
                    return ("zip", "Sauvegarde importée dans la liste.", dest);
                }

                if (ext is ".opml" or ".xml")
                {
                    var (p, s) = await ImportOpmlAsync(await File.ReadAllTextAsync(tmp));
                    return ("opml", $"{s} sources ajoutées dans {p} catégories.", string.Empty);
                }

                if (ext is ".db3" or ".db" or ".sqlite" or ".sqlite3")
                {
                    var kind = await SQLiteService.DetectDatabaseKindAsync(tmp);
                    switch (kind)
                    {
                        case "config":
                            var (pl, si) = await _db.MergeConfigFromDatabaseFileAsync(tmp);
                            return ("config", $"Configuration fusionnée : {pl} catégories et {si} sources ajoutées.", string.Empty);
                        case "articles":
                            int n = await _db.MergeArticlesFromDatabaseFileAsync(tmp);
                            return ("articles", $"{n} articles ajoutés (doublons ignorés).", string.Empty);
                        case "archives":
                            int a = await _db.MergeArchivesFromDatabaseFileAsync(tmp);
                            return ("archives", $"{a} archives ajoutées.", string.Empty);
                        default:
                            return ("", "Ce fichier n'est pas une base Rss Prout reconnue.", string.Empty);
                    }
                }

                return ("", "Format non pris en charge (.zip, .db3, .opml).", string.Empty);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        // =================================================================
        // OPML
        // =================================================================

        public async Task<(int playlists, int sites)> ImportOpmlAsync(string content)
        {
            var data = _opml.ImportOpml(content);
            var existingPlaylists = await _db.GetPlaylistsAsync();
            var existingSites = await _db.GetSitesAsync();
            var urls = new HashSet<string>(existingSites.Select(s => s.FeedUrl.TrimEnd('/')), StringComparer.OrdinalIgnoreCase);

            int p = 0, s = 0;
            foreach (var d in data)
            {
                var playlist = existingPlaylists.FirstOrDefault(x => string.Equals(x.Name, d.Name, StringComparison.CurrentCultureIgnoreCase));
                if (playlist == null)
                {
                    playlist = new FeedPlaylist { Name = d.Name, IsActive = true };
                    await _db.SavePlaylistAsync(playlist);
                    existingPlaylists.Add(playlist);
                    p++;
                }

                foreach (var site in d.Sites)
                {
                    if (urls.Contains(site.FeedUrl.TrimEnd('/'))) continue;
                    site.PlaylistId = playlist.Id;
                    await _db.SaveSiteAsync(site);
                    urls.Add(site.FeedUrl.TrimEnd('/'));
                    s++;
                }
            }
            return (p, s);
        }

        public async Task<string> ExportOpmlAsync()
        {
            var playlists = await _db.GetPlaylistsAsync();
            var dict = new Dictionary<FeedPlaylist, List<FeedSite>>();
            foreach (var p in playlists)
                dict[p] = await _db.GetSitesForPlaylistAsync(p.Id);

            var xml = _opml.ExportPlaylistsToOpml(dict);
            Directory.CreateDirectory(SQLiteService.ExportFolder);
            var path = Path.Combine(SQLiteService.ExportFolder, $"RssProut_Flux_{DateTime.Now:yyyyMMdd}.opml");
            await File.WriteAllTextAsync(path, xml, new UTF8Encoding(false));
            return path;
        }

        // =================================================================
        // EXPORTS LISIBLES
        // =================================================================

        /// <summary>Exporte des articles en JSON (titre, lien, date, résumé, contenu optionnel).</summary>
        public async Task<string> ExportArticlesJsonAsync(ArticleQuery query, bool includeContent)
        {
            var items = await _db.QueryArticlesAsync(query);
            var list = new List<Dictionary<string, object?>>();
            foreach (var i in items)
            {
                string? content = null;
                if (includeContent && i.IsDownloaded)
                    content = (await _db.GetItemByIdAsync(i.Id))?.ContentHtml;

                list.Add(new Dictionary<string, object?>
                {
                    ["title"] = i.Title,
                    ["link"] = i.Link,
                    ["site"] = i.SiteName,
                    ["author"] = i.Author,
                    ["published"] = DateTime.SpecifyKind(i.PublishDateUtc, DateTimeKind.Utc).ToString("o"),
                    ["read"] = i.IsRead,
                    ["favorite"] = i.IsFavorite,
                    ["readLater"] = i.IsReadLater,
                    ["summary"] = i.Summary,
                    ["contentHtml"] = content
                });
            }

            Directory.CreateDirectory(SQLiteService.ExportFolder);
            var path = Path.Combine(SQLiteService.ExportFolder, $"RssProut_Articles_{DateTime.Now:yyyyMMdd_HHmm}.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(list, JsonOptions), new UTF8Encoding(false));
            return path;
        }

        /// <summary>Exporte des articles (favoris, archives…) en une page HTML lisible partout.</summary>
        public async Task<string> ExportHtmlAsync(string title, IEnumerable<(string title, string site, DateTime date, string link, string html)> articles)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html lang='fr'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1'>");
            sb.Append("<title>").Append(HtmlHelper.Encode(title)).Append("</title><style>");
            sb.Append("body{font-family:Georgia,serif;max-width:760px;margin:0 auto;padding:24px;line-height:1.65;color:#1c1d21;background:#f6f4ef}");
            sb.Append("h1{font-family:system-ui,sans-serif}article{background:#fff;border-radius:14px;padding:22px;margin:22px 0;box-shadow:0 1px 3px rgba(0,0,0,.08)}");
            sb.Append(".meta{font:13px system-ui,sans-serif;color:#6b7280}img{max-width:100%;height:auto;border-radius:10px}a{color:#e8702a}nav a{display:block;margin:4px 0}");
            sb.Append("</style></head><body>");
            sb.Append("<h1>").Append(HtmlHelper.Encode(title)).Append("</h1>");
            sb.Append("<p class='meta'>Exporté le ").Append(DateTime.Now.ToString("dd/MM/yyyy HH:mm")).Append(" depuis Rss Prout</p>");

            var list = articles.ToList();
            sb.Append("<nav>");
            for (int i = 0; i < list.Count; i++)
                sb.Append($"<a href='#a{i}'>").Append(HtmlHelper.Encode(list[i].title)).Append("</a>");
            sb.Append("</nav>");

            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                // Les images locales ne sont pas lisibles hors de l'app : on remet l'URL d'origine
                var body = System.Text.RegularExpressions.Regex.Replace(a.html ?? string.Empty,
                    @"src=""file://[^""]+""\s+data-orig-src=""([^""]+)""|data-orig-src=""([^""]+)""\s+src=""file://[^""]+""",
                    m => $"src=\"{(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)}\"");
                sb.Append($"<article id='a{i}'><h2>").Append(HtmlHelper.Encode(a.title)).Append("</h2>");
                sb.Append("<p class='meta'>").Append(HtmlHelper.Encode(a.site)).Append(" · ").Append(a.date.ToString("dd/MM/yyyy"));
                if (!string.IsNullOrEmpty(a.link)) sb.Append($" · <a href='{HtmlHelper.Encode(a.link)}'>Article original</a>");
                sb.Append("</p>").Append(body).Append("</article>");
            }
            sb.Append("</body></html>");

            Directory.CreateDirectory(SQLiteService.ExportFolder);
            var path = Path.Combine(SQLiteService.ExportFolder, $"RssProut_{FormatHelper.SafeFileName(title)}_{DateTime.Now:yyyyMMdd_HHmm}.html");
            await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }

        /// <summary>Copie une base telle quelle (export d'un seul fichier).</summary>
        public async Task<string> ExportSingleDatabaseAsync(string which)
        {
            Directory.CreateDirectory(SQLiteService.ExportFolder);
            var name = which switch { "config" => "RssConfig", "articles" => "RssArticles", _ => "RssArchives" };
            var path = Path.Combine(SQLiteService.ExportFolder, $"{name}_{DateTime.Now:yyyyMMdd_HHmm}.db3");
            await _db.SnapshotAsync(which, path);
            return path;
        }

        public static async Task ShareFileAsync(string path, string title)
        {
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = title,
                File = new ShareFile(path)
            });
        }
    }
}
