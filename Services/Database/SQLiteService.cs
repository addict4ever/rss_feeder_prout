using Rss_feeder_prout.Models;
using Rss_feeder_prout.Helpers;
using SQLite;
using System.Diagnostics;
using Microsoft.Maui.Storage;

namespace Rss_feeder_prout.Services
{
    /// <summary>
    /// Accès aux bases SQLite — NOUVELLE ORGANISATION :
    ///
    ///   AppData/
    ///     Databases/RssConfig.db3    → catégories, sources, journal de synchro
    ///     Databases/RssArticles.db3  → TOUS les articles (une seule base, index unique Site+Guid)
    ///     Databases/RssArchives.db3  → archives permanentes
    ///     ImageCache/                → images hors-ligne (nom = hash SHA1 stable)
    ///     Icons/                     → icônes des sites
    ///     Backups/                   → sauvegardes .zip
    ///     ColdArchives/              → anciens articles déplacés par mois (.db3 ou .zip)
    ///     Legacy/                    → anciennes bases quotidiennes RssProut_*.db3 après migration
    ///
    /// L'ancien système (une base par jour) causait des collisions d'Id entre fichiers,
    /// des mises à jour perdues (lu/non lu) et des recherches très lentes.
    /// </summary>
    public partial class SQLiteService
    {
        private const int SchemaVersion = 3;

        private const SQLiteOpenFlags DbFlags = SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex;

        private SQLiteAsyncConnection? _configDb;   // Sites, playlists, journal
        private SQLiteAsyncConnection? _articlesDb; // Articles
        private SQLiteAsyncConnection? _archiveDb;  // Archives

        private readonly SemaphoreSlim _initSemaphore = new(1, 1);
        private volatile bool _isInitialized;

        /// <summary>Déclenché quand la base est remplacée (restauration, réinitialisation).</summary>
        public event EventHandler? DatabaseReset;

        /// <summary>Déclenché quand des articles changent (pour rafraîchir les compteurs).</summary>
        public event EventHandler? ArticlesChanged;

        // ------------------------------------------------------------------
        // Chemins
        // ------------------------------------------------------------------

        public static string AppDataFolder => FileSystem.AppDataDirectory;
        public static string DatabaseFolder => Path.Combine(AppDataFolder, "Databases");
        public static string ImageCacheFolder => Path.Combine(AppDataFolder, "ImageCache");
        public static string IconsFolder => Path.Combine(AppDataFolder, "Icons");
        public static string BackupsFolder => Path.Combine(AppDataFolder, "Backups");
        public static string ColdArchiveFolder => Path.Combine(AppDataFolder, "ColdArchives");
        public static string LegacyFolder => Path.Combine(AppDataFolder, "Legacy");
        public static string ExportFolder => Path.Combine(FileSystem.CacheDirectory, "Exports");

        public string ConfigDbPath => Path.Combine(DatabaseFolder, "RssConfig.db3");
        public string ArticlesDbPath => Path.Combine(DatabaseFolder, "RssArticles.db3");
        public string ArchiveDbPath => Path.Combine(DatabaseFolder, "RssArchives.db3");

        // Anciens emplacements (version précédente)
        private static string LegacyConfigPath => Path.Combine(AppDataFolder, "RssConfig.db3");
        private static string LegacyArchivePath => Path.Combine(AppDataFolder, "Archives.db3");
        private static string LegacyIndexPath => Path.Combine(AppDataFolder, "Articles.db3");

        public SQLiteService()
        {
        }

        // ------------------------------------------------------------------
        // Initialisation
        // ------------------------------------------------------------------

        /// <summary>Conservé : ancien nom utilisé partout.</summary>
        private Task Init() => EnsureInitialized();

        public async Task EnsureInitialized()
        {
            if (_isInitialized) return;

            await _initSemaphore.WaitAsync();
            try
            {
                if (!_isInitialized)
                {
                    await InitCoreAsync();
                    _isInitialized = true;
                }
            }
            finally
            {
                _initSemaphore.Release();
            }
        }

        private async Task InitCoreAsync()
        {
            EnsureFolders();
            MoveLegacyRootDatabases();

            bool configIsNew = !File.Exists(ConfigDbPath);

            // 1. CONFIGURATION
            _configDb = new SQLiteAsyncConnection(ConfigDbPath, DbFlags);
            await PrepareConnectionAsync(_configDb);
            await _configDb.CreateTableAsync<FeedPlaylist>();
            await _configDb.CreateTableAsync<FeedSite>();
            await _configDb.CreateTableAsync<SyncLogEntry>();

            if (configIsNew && await _configDb.Table<FeedSite>().CountAsync() == 0)
            {
                await CreateDefaultData();
            }

            // 2. ARTICLES (base unique)
            _articlesDb = new SQLiteAsyncConnection(ArticlesDbPath, DbFlags);
            await PrepareConnectionAsync(_articlesDb);
            await _articlesDb.CreateTableAsync<RssItem>();

            // 3. ARCHIVES
            _archiveDb = new SQLiteAsyncConnection(ArchiveDbPath, DbFlags);
            await PrepareConnectionAsync(_archiveDb);
            await _archiveDb.CreateTableAsync<ArchiveItem>();

            // 4. Migration des anciennes bases quotidiennes (RssProut_AAAA_MM_JJ.db3)
            await MigrateLegacyDailyDatabasesAsync();
            await UpgradeSchemaAsync();
        }

        private static async Task PrepareConnectionAsync(SQLiteAsyncConnection conn)
        {
            try
            {
                await conn.ExecuteScalarAsync<string>("PRAGMA journal_mode=WAL;"); // Mode WAL actif
                await conn.ExecuteAsync("PRAGMA synchronous=NORMAL;");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB] PRAGMA: {ex.Message}");
            }
        }

        private static void EnsureFolders()
        {
            foreach (var dir in new[] { DatabaseFolder, ImageCacheFolder, IconsFolder, BackupsFolder, ColdArchiveFolder, LegacyFolder })
            {
                try { Directory.CreateDirectory(dir); } catch (Exception ex) { Debug.WriteLine($"[DB] Dossier {dir}: {ex.Message}"); }
            }
        }

        /// <summary>
        /// Déplace les bases de l'ancienne version (racine AppData) vers Databases/ (avec leurs fichiers -wal / -shm).
        /// </summary>
        private void MoveLegacyRootDatabases()
        {
            MoveDbFile(LegacyConfigPath, ConfigDbPath);
            MoveDbFile(LegacyArchivePath, ArchiveDbPath);
            MoveDbFile(LegacyIndexPath, Path.Combine(LegacyFolder, "Articles_index_ancien.db3"));
        }

        private static void MoveDbFile(string source, string target)
        {
            try
            {
                if (!File.Exists(source) || File.Exists(target)) return;

                File.Move(source, target);
                foreach (var suffix in new[] { "-wal", "-shm" })
                {
                    if (File.Exists(source + suffix))
                        File.Move(source + suffix, target + suffix, true);
                }
                Debug.WriteLine($"[DB] Déplacé : {Path.GetFileName(source)} → {target}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB] Déplacement impossible {source}: {ex.Message}");
            }
        }

        private async Task UpgradeSchemaAsync()
        {
            if (_articlesDb == null) return;
            try
            {
                int version = await _articlesDb.ExecuteScalarAsync<int>("PRAGMA user_version;");
                if (version >= SchemaVersion) return;

                // Remplit les nouvelles colonnes pour les lignes existantes
                await _articlesDb.ExecuteAsync("UPDATE RssItem SET FetchedAtUtc = PublishDateUtc WHERE FetchedAtUtc = 0 OR FetchedAtUtc IS NULL;");

                // Index supplémentaires
                await _articlesDb.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_RssItem_Read_Date ON RssItem (IsRead, PublishDateUtc);");
                await _articlesDb.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_RssItem_Fav ON RssItem (IsFavorite);");
                await _articlesDb.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_RssItem_Later ON RssItem (IsReadLater);");

                await _articlesDb.ExecuteAsync($"PRAGMA user_version = {SchemaVersion};");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB] Upgrade schéma : {ex.Message}");
            }
        }

        /// <summary>
        /// Fusionne chaque ancienne base quotidienne dans RssArticles.db3 puis la range dans Legacy/.
        /// </summary>
        private async Task MigrateLegacyDailyDatabasesAsync()
        {
            string[] dailyFiles;
            try
            {
                dailyFiles = Directory.GetFiles(AppDataFolder, "RssProut_*.db3");
            }
            catch
            {
                return;
            }

            foreach (var file in dailyFiles.OrderBy(f => f))
            {
                try
                {
                    int merged = await MergeArticlesFromDatabaseFileAsync(file);
                    Debug.WriteLine($"[MIGRATION] {Path.GetFileName(file)} : {merged} articles fusionnés.");
                    MoveDbFile(file, Path.Combine(LegacyFolder, Path.GetFileName(file)));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MIGRATION] Échec {file}: {ex.Message}");
                }
            }
        }

        // ------------------------------------------------------------------
        // Gestion des connexions (sauvegarde / restauration)
        // ------------------------------------------------------------------

        private SQLiteAsyncConnection ConfigDb => _configDb ?? throw new InvalidOperationException("Base de configuration non initialisée.");
        private SQLiteAsyncConnection ArticlesDb => _articlesDb ?? throw new InvalidOperationException("Base d'articles non initialisée.");
        private SQLiteAsyncConnection ArchiveDb => _archiveDb ?? throw new InvalidOperationException("Base d'archives non initialisée.");

        /// <summary>Ferme proprement toutes les connexions (avant remplacement des fichiers).</summary>
        public async Task CloseAllAsync()
        {
            await _initSemaphore.WaitAsync();
            try
            {
                foreach (var conn in new[] { _configDb, _articlesDb, _archiveDb })
                {
                    if (conn == null) continue;
                    try { await conn.ExecuteScalarAsync<int>("PRAGMA wal_checkpoint(TRUNCATE);"); } catch { }
                    try { await conn.CloseAsync(); } catch { }
                }
                _configDb = null;
                _articlesDb = null;
                _archiveDb = null;
                _isInitialized = false;
            }
            finally
            {
                _initSemaphore.Release();
            }
        }

        /// <summary>Rouvre les bases après une restauration.</summary>
        public async Task ReopenAsync()
        {
            await EnsureInitialized();
            DatabaseReset?.Invoke(this, EventArgs.Empty);
        }

        protected void RaiseArticlesChanged() => ArticlesChanged?.Invoke(this, EventArgs.Empty);

        /// <summary>Force l'écriture du journal WAL dans les fichiers .db3.</summary>
        public async Task CheckpointAllAsync()
        {
            await EnsureInitialized();
            foreach (var conn in new[] { _configDb, _articlesDb, _archiveDb })
            {
                if (conn == null) continue;
                try { await conn.ExecuteScalarAsync<int>("PRAGMA wal_checkpoint(TRUNCATE);"); }
                catch (Exception ex) { Debug.WriteLine($"[DB] Checkpoint: {ex.Message}"); }
            }
        }

        /// <summary>
        /// Copie cohérente et compactée d'une base (VACUUM INTO). Retombe sur une copie simple en cas d'échec.
        /// </summary>
        public async Task SnapshotAsync(string which, string destinationPath)
        {
            await EnsureInitialized();
            var conn = which switch
            {
                "config" => ConfigDb,
                "articles" => ArticlesDb,
                "archives" => ArchiveDb,
                _ => throw new ArgumentException(which)
            };
            var source = which switch
            {
                "config" => ConfigDbPath,
                "articles" => ArticlesDbPath,
                _ => ArchiveDbPath
            };

            if (File.Exists(destinationPath)) File.Delete(destinationPath);

            try
            {
                var escaped = destinationPath.Replace("'", "''");
                await conn.ExecuteAsync($"VACUUM INTO '{escaped}';");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DB] VACUUM INTO indisponible ({ex.Message}), copie simple.");
                try { await conn.ExecuteScalarAsync<int>("PRAGMA wal_checkpoint(TRUNCATE);"); } catch { }
                File.Copy(source, destinationPath, true);
            }
        }

        public string GetDatabasePath()
        {
            // On retourne le chemin de la base de configuration par défaut
            return ConfigDbPath;
        }
    }
}
