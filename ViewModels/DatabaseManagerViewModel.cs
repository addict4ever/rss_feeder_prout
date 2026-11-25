using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using Rss_feeder_prout.Helpers;
using Rss_feeder_prout.Models;
using Rss_feeder_prout.Services;

namespace Rss_feeder_prout.ViewModels
{
    /// <summary>
    /// Base de données, sauvegardes et maintenance :
    /// stockage détaillé, sauvegarde/restauration (.zip), import (.zip/.db3/.opml), exports,
    /// compactage, vérification d'intégrité, archives froides par mois, nettoyage, réinitialisation.
    /// </summary>
    public class DatabaseManagerViewModel : BaseViewModel
    {
        private readonly SQLiteService _dbService;
        private readonly BackupService _backup;
        private readonly ImageCacheService _images;

        public ObservableCollection<DbFileInfo> DatabaseFiles { get; } = new();
        public ObservableCollection<BackupInfo> Backups { get; } = new();
        public ObservableCollection<DbFileInfo> ColdArchives { get; } = new();

        // --- Stockage ---
        private string _totalText = "…";
        public string TotalText { get => _totalText; set => SetProperty(ref _totalText, value); }

        private string _databasesText = "…";
        public string DatabasesText { get => _databasesText; set => SetProperty(ref _databasesText, value); }

        private string _imagesText = "…";
        public string ImagesText { get => _imagesText; set => SetProperty(ref _imagesText, value); }

        private string _backupsText = "…";
        public string BackupsText { get => _backupsText; set => SetProperty(ref _backupsText, value); }

        private string _articlesText = "…";
        public string ArticlesText { get => _articlesText; set => SetProperty(ref _articlesText, value); }

        private string _coldText = "…";
        public string ColdText { get => _coldText; set => SetProperty(ref _coldText, value); }

        private double _dbRatio, _imgRatio, _backupRatio, _otherRatio;
        public double DbRatio { get => _dbRatio; set => SetProperty(ref _dbRatio, value); }
        public double ImgRatio { get => _imgRatio; set => SetProperty(ref _imgRatio, value); }
        public double BackupRatio { get => _backupRatio; set => SetProperty(ref _backupRatio, value); }
        public double OtherRatio { get => _otherRatio; set => SetProperty(ref _otherRatio, value); }

        private string _statusMessage = string.Empty;
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        // --- Options de sauvegarde ---
        private bool _backupImages;
        public bool BackupImages { get => _backupImages; set => SetProperty(ref _backupImages, value); }

        private bool _backupArticles = true;
        public bool BackupArticles { get => _backupArticles; set => SetProperty(ref _backupArticles, value); }

        private int _compressionIndex = 1;
        /// <summary>0 = rapide, 1 = équilibrée, 2 = maximale.</summary>
        public int CompressionIndex { get => _compressionIndex; set => SetProperty(ref _compressionIndex, value); }

        public List<string> CompressionChoices { get; } = new() { "Rapide", "Équilibrée", "Maximale" };

        public bool AutoBackupEnabled
        {
            get => AppSettings.AutoBackupEnabled;
            set { AppSettings.AutoBackupEnabled = value; OnPropertyChanged(); }
        }

        public bool HasBackups => Backups.Count > 0;
        public bool HasColdArchives => ColdArchives.Count > 0;

        // --- Commandes (anciens noms conservés) ---
        public ICommand ExportDbCommand { get; }
        public ICommand DeleteDbCommand { get; }
        public ICommand AdvancedCleanupCommand { get; }
        public ICommand CleanDatabaseCommand { get; }
        public ICommand RefreshDbListCommand { get; }
        public ICommand ImportDbCommand { get; }
        public ICommand CreateBackupCommand { get; }
        public ICommand BackupMenuCommand { get; }
        public ICommand VacuumCommand { get; }
        public ICommand IntegrityCommand { get; }
        public ICommand ColdArchiveCommand { get; }
        public ICommand ColdMenuCommand { get; }
        public ICommand ClearImagesCommand { get; }
        public ICommand OrphanImagesCommand { get; }
        public ICommand DeleteLegacyCommand { get; }
        public ICommand ExportOpmlCommand { get; }
        public ICommand ExportArticlesCommand { get; }
        public ICommand ClearAllArticlesCommand { get; }

        public DatabaseManagerViewModel(SQLiteService dbService, BackupService backup, ImageCacheService images)
        {
            _dbService = dbService;
            _backup = backup;
            _images = images;
            Title = "Données & sauvegardes";

            ExportDbCommand = new Command(async () => await ExecuteExportDbCommand());
            DeleteDbCommand = new Command(async () => await ExecuteDeleteDbCommand());
            AdvancedCleanupCommand = new Command<string>(async (mode) => await ExecuteAdvancedCleanup(mode));
            CleanDatabaseCommand = new Command<string>(async (mode) => await ExecuteAdvancedCleanup(mode));
            RefreshDbListCommand = new Command(async () => await RefreshAsync());
            ImportDbCommand = new Command(async () => await ExecuteImportDbCommand());
            CreateBackupCommand = new Command(async () => await CreateBackupAsync());
            BackupMenuCommand = new Command<BackupInfo>(async b => await BackupMenuAsync(b));
            VacuumCommand = new Command(async () => await VacuumAsync());
            IntegrityCommand = new Command(async () => await IntegrityAsync());
            ColdArchiveCommand = new Command(async () => await ColdArchiveAsync());
            ColdMenuCommand = new Command<DbFileInfo>(async f => await ColdMenuAsync(f));
            ClearImagesCommand = new Command(async () => await ClearImagesAsync());
            OrphanImagesCommand = new Command(async () => await OrphanImagesAsync());
            DeleteLegacyCommand = new Command(async () => await DeleteLegacyAsync());
            ExportOpmlCommand = new Command(async () => await RunAsync("Export OPML", async () =>
            {
                var path = await _backup.ExportOpmlAsync();
                await BackupService.ShareFileAsync(path, "Mes flux (OPML)");
            }));
            ExportArticlesCommand = new Command(async () => await ExportArticlesAsync());
            ClearAllArticlesCommand = new Command(async () => await ClearAllArticlesAsync());
        }

        // =================================================================
        // Rafraîchissement
        // =================================================================

        public async Task RefreshAsync()
        {
            try
            {
                var report = await _dbService.GetStorageReportAsync();
                var files = _dbService.ListDatabaseFiles();
                var backups = _backup.ListBackups();
                var cold = _dbService.ListColdArchives();

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    TotalText = FormatHelper.Bytes(report.TotalBytes);
                    DatabasesText = FormatHelper.Bytes(report.DatabasesBytes);
                    ImagesText = $"{FormatHelper.Bytes(report.ImageCacheBytes + report.IconsBytes)} · {report.ImageCacheCount} images";
                    BackupsText = $"{FormatHelper.Bytes(report.BackupsBytes)} · {report.BackupCount} sauvegardes";
                    ColdText = $"{FormatHelper.Bytes(report.ColdArchiveBytes)} · {report.ColdArchiveCount} fichiers";
                    ArticlesText = $"{report.ArticleCount:N0} articles ({report.DownloadedCount:N0} hors-ligne) · {report.ArchiveCount:N0} archives · {report.SiteCount} sources";

                    double total = Math.Max(1, report.TotalBytes);
                    DbRatio = report.DatabasesBytes / total;
                    ImgRatio = (report.ImageCacheBytes + report.IconsBytes) / total;
                    BackupRatio = report.BackupsBytes / total;
                    OtherRatio = (report.ColdArchiveBytes + report.LegacyBytes) / total;

                    DatabaseFiles.Clear();
                    foreach (var f in files) DatabaseFiles.Add(f);

                    Backups.Clear();
                    foreach (var b in backups) Backups.Add(b);

                    ColdArchives.Clear();
                    foreach (var c in cold) ColdArchives.Add(c);

                    Notify(nameof(HasBackups), nameof(HasColdArchives));
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur listing DB: {ex.Message}");
            }
        }

        /// <summary>Conservé : rafraîchit la liste des fichiers.</summary>
        private void ExecuteRefreshDbList() => _ = RefreshAsync();

        private async Task RunAsync(string label, Func<Task> action)
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusMessage = label + "…";
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                await UiHelper.AlertAsync("Erreur", $"{label} : {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                StatusMessage = string.Empty;
                await RefreshAsync();
            }
        }

        // =================================================================
        // Sauvegardes
        // =================================================================

        private async Task CreateBackupAsync()
        {
            await RunAsync("Sauvegarde", async () =>
            {
                var level = CompressionIndex switch
                {
                    0 => CompressionLevel.Fastest,
                    2 => CompressionLevel.SmallestSize,
                    _ => CompressionLevel.Optimal
                };
                var progress = new Progress<string>(s => StatusMessage = s);
                var path = await _backup.CreateBackupAsync(new BackupOptions
                {
                    IncludeArticles = BackupArticles,
                    IncludeArchives = true,
                    IncludeImages = BackupImages,
                    IncludeSettings = true,
                    Level = level
                }, progress);

                var size = FormatHelper.Bytes(new FileInfo(path).Length);
                if (await UiHelper.ConfirmAsync("Sauvegarde créée", $"{Path.GetFileName(path)} ({size}).\n\nLa partager maintenant (Drive, e-mail…) ?", "Partager", "Plus tard"))
                    await BackupService.ShareFileAsync(path, "Sauvegarde Rss Prout");
            });
        }

        private async Task BackupMenuAsync(BackupInfo? b)
        {
            if (b == null) return;
            var description = _backup.Describe(b.FullPath);
            var choice = await UiHelper.ActionSheetAsync($"{b.FileName}\n{description}", "Annuler", "Supprimer",
                "Restaurer (remplacer)", "Restaurer en fusionnant les articles", "Partager / enregistrer ailleurs");

            switch (choice)
            {
                case "Restaurer (remplacer)":
                case "Restaurer en fusionnant les articles":
                    bool merge = choice.Contains("fusionnant");
                    if (!await UiHelper.ConfirmAsync("Restaurer",
                        merge ? "La configuration et les archives seront remplacées ; les articles de la sauvegarde seront AJOUTÉS aux vôtres.\n\nUne sauvegarde de sécurité sera créée avant."
                              : "Vos bases actuelles seront REMPLACÉES par celles de la sauvegarde.\n\nUne sauvegarde de sécurité sera créée avant.",
                        "Restaurer", "Annuler")) return;

                    await RunAsync("Restauration", async () =>
                    {
                        var progress = new Progress<string>(s => StatusMessage = s);
                        var msg = await _backup.RestoreBackupAsync(b.FullPath, new RestoreOptions { MergeArticles = merge }, progress);
                        ThemeService.Apply();
                        ServiceHelper.Get<SyncScheduler>()?.Reschedule();
                        await UiHelper.AlertAsync("Restauration terminée", msg);
                    });
                    break;

                case "Partager / enregistrer ailleurs":
                    await BackupService.ShareFileAsync(b.FullPath, "Sauvegarde Rss Prout");
                    break;

                case "Supprimer":
                    if (await UiHelper.ConfirmAsync("Supprimer", $"Supprimer {b.FileName} ?", "Supprimer", "Annuler"))
                    {
                        _backup.DeleteBackup(b.FullPath);
                        await RefreshAsync();
                    }
                    break;
            }
        }

        /// <summary>Conservé : import d'un fichier (.zip, .db3, .opml) avec détection automatique.</summary>
        private async Task ExecuteImportDbCommand()
        {
            FileResult? file;
            try
            {
                file = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "Sauvegarde (.zip), base (.db3) ou flux (.opml)"
                });
            }
            catch (Exception ex)
            {
                await UiHelper.AlertAsync("Erreur", ex.Message);
                return;
            }
            if (file == null) return;

            await RunAsync("Import", async () =>
            {
                var (kind, message, path) = await _backup.ImportExternalFileAsync(file);
                if (kind == "zip")
                {
                    await RefreshAsync();
                    var info = _backup.ListBackups().FirstOrDefault(x => x.FullPath == path);
                    if (info != null && await UiHelper.ConfirmAsync("Sauvegarde importée", $"{_backup.Describe(path)}\n\nLa restaurer maintenant ?", "Restaurer…", "Plus tard"))
                    {
                        IsBusy = false;
                        await BackupMenuAsync(info);
                    }
                }
                else
                {
                    await UiHelper.AlertAsync("Import", message);
                }
            });
        }

        /// <summary>Conservé : exports (OPML, JSON, bases individuelles, sauvegarde complète).</summary>
        private async Task ExecuteExportDbCommand()
        {
            var action = await UiHelper.ActionSheetAsync("Quel export choisir ?", "Annuler", null,
                "Sauvegarde complète (.zip)",
                "Flux au format OPML",
                "Articles en JSON",
                "Base de configuration (.db3)",
                "Base des articles (.db3)",
                "Base des archives (.db3)");
            if (action == null) return;

            await RunAsync("Export", async () =>
            {
                string path;
                switch (action)
                {
                    case "Sauvegarde complète (.zip)":
                        path = await _backup.CreateBackupAsync(new BackupOptions { IncludeImages = BackupImages });
                        break;
                    case "Flux au format OPML":
                        path = await _backup.ExportOpmlAsync();
                        break;
                    case "Articles en JSON":
                        path = await _backup.ExportArticlesJsonAsync(new ArticleQuery { Limit = 100000 }, includeContent: false);
                        break;
                    case "Base de configuration (.db3)":
                        path = await _backup.ExportSingleDatabaseAsync("config");
                        break;
                    case "Base des articles (.db3)":
                        path = await _backup.ExportSingleDatabaseAsync("articles");
                        break;
                    default:
                        path = await _backup.ExportSingleDatabaseAsync("archives");
                        break;
                }
                await BackupService.ShareFileAsync(path, "Export Rss Prout");
            });
        }

        private async Task ExportArticlesAsync()
        {
            var choice = await UiHelper.ActionSheetAsync("Exporter les articles", "Annuler", null,
                "Tous (JSON, sans contenu)", "Favoris (JSON, avec contenu)", "Hors-ligne (JSON, avec contenu)");
            if (choice == null) return;

            await RunAsync("Export", async () =>
            {
                var q = new ArticleQuery { Limit = 100000 };
                bool content = false;
                if (choice.StartsWith("Favoris")) { q.Filter = ArticleFilter.Favorites; content = true; }
                else if (choice.StartsWith("Hors-ligne")) { q.Filter = ArticleFilter.Downloaded; content = true; q.Limit = 2000; }
                var path = await _backup.ExportArticlesJsonAsync(q, content);
                await BackupService.ShareFileAsync(path, "Articles Rss Prout");
            });
        }

        // =================================================================
        // Maintenance
        // =================================================================

        private async Task VacuumAsync()
        {
            await RunAsync("Compactage", async () =>
            {
                var (before, after) = await _dbService.VacuumAllAsync();
                await UiHelper.AlertAsync("Bases compactées",
                    $"Avant : {FormatHelper.Bytes(before)}\nAprès : {FormatHelper.Bytes(after)}\nGain : {FormatHelper.Bytes(Math.Max(0, before - after))}");
            });
        }

        private async Task IntegrityAsync()
        {
            await RunAsync("Vérification", async () =>
            {
                var r = await _dbService.IntegrityCheckAsync();
                var ok = r.Values.All(v => v == "ok");
                await UiHelper.AlertAsync(ok ? "✅ Bases saines" : "⚠️ Problème détecté",
                    string.Join("\n", r.Select(kv => $"{kv.Key} : {kv.Value}")));
            });
        }

        private async Task ColdArchiveAsync()
        {
            var choice = await UiHelper.ActionSheetAsync("Archiver les anciens articles par mois\n(favoris et « plus tard » conservés)", "Annuler", null,
                "Plus de 1 mois", "Plus de 3 mois", "Plus de 6 mois", "Plus de 1 an");
            if (choice == null) return;
            int days = choice switch { "Plus de 1 mois" => 30, "Plus de 3 mois" => 90, "Plus de 6 mois" => 180, _ => 365 };
            bool compress = await UiHelper.ConfirmAsync("Compression", "Compresser les fichiers d'archive (.zip) ? Ils prennent beaucoup moins de place.", "Compresser", "Non");

            await RunAsync("Archivage", async () =>
            {
                var (moved, files) = await _dbService.MoveOldArticlesToColdArchiveAsync(days, compress);
                if (moved > 0) await _dbService.VacuumAllAsync();
                await UiHelper.AlertAsync("Archivage", moved == 0 ? "Aucun article assez ancien." : $"{moved} articles déplacés dans {files} fichier(s) mensuel(s).");
            });
        }

        private async Task ColdMenuAsync(DbFileInfo? f)
        {
            if (f == null) return;
            var choice = await UiHelper.ActionSheetAsync(f.Name, "Annuler", "Supprimer", "Réintégrer dans la base", "Partager");
            switch (choice)
            {
                case "Réintégrer dans la base":
                    await RunAsync("Réintégration", async () =>
                    {
                        int n = await _dbService.RestoreColdArchiveAsync(f.FullPath);
                        await UiHelper.AlertAsync("Réintégration", $"{n} articles réintégrés.");
                    });
                    break;
                case "Partager":
                    await BackupService.ShareFileAsync(f.FullPath, f.Name);
                    break;
                case "Supprimer":
                    if (await UiHelper.ConfirmAsync("Supprimer", $"Supprimer définitivement {f.Name} ?", "Supprimer", "Annuler"))
                    {
                        await _dbService.DeleteColdArchiveAsync(f.FullPath);
                        await RefreshAsync();
                    }
                    break;
            }
        }

        private async Task ClearImagesAsync()
        {
            var choice = await UiHelper.ActionSheetAsync("Cache d'images", "Annuler", null,
                "Images de plus de 30 jours", "Images de plus de 7 jours", "Tout le cache d'images");
            if (choice == null) return;
            int days = choice.Contains("30") ? 30 : choice.Contains("7") ? 7 : 0;
            await RunAsync("Nettoyage des images", async () =>
            {
                var (count, bytes) = await _images.ClearCacheAsync(days);
                await UiHelper.AlertAsync("Images", $"{count} images supprimées ({FormatHelper.Bytes(bytes)} libérés).\nLes articles afficheront les images en ligne si disponibles.");
            });
        }

        private async Task OrphanImagesAsync()
        {
            await RunAsync("Recherche des images inutilisées", async () =>
            {
                var (deleted, freed) = await _dbService.CleanOrphanImagesAsync();
                await UiHelper.AlertAsync("Images inutilisées", $"{deleted} images supprimées ({FormatHelper.Bytes(freed)} libérés).");
            });
        }

        private async Task DeleteLegacyAsync()
        {
            if (!await UiHelper.ConfirmAsync("Anciennes bases",
                "Supprimer les anciennes bases (déjà migrées dans la nouvelle organisation) ?", "Supprimer", "Annuler")) return;
            await RunAsync("Suppression", async () =>
            {
                int n = await _dbService.DeleteLegacyFilesAsync();
                await UiHelper.AlertAsync("Anciennes bases", $"{n} fichiers supprimés.");
            });
        }

        /// <summary>
        /// Conservé (CORRIGÉ : les boutons appelaient une commande inexistante) : nettoyage par ancienneté.
        /// Modes : 1d, 2d, 3d, 7d, 1w, 1m, 3m, ReadOnly, Archive6m, All, full_cache.
        /// </summary>
        private async Task ExecuteAdvancedCleanup(string? mode)
        {
            if (string.IsNullOrEmpty(mode)) return;

            string label = mode switch
            {
                "ReadOnly" => "Supprimer tous les articles LUS (favoris conservés) ?",
                "Archive6m" => "Supprimer les archives de plus de 6 mois ?",
                "All" or "full_cache" => "Vider TOUS les articles (favoris et archives conservés) ?",
                _ => $"Supprimer les articles de plus de {DescribeMode(mode)} (favoris conservés) ?"
            };
            if (!await UiHelper.ConfirmAsync("Nettoyage", label, "Supprimer", "Annuler")) return;

            await RunAsync("Nettoyage", async () =>
            {
                int n = mode switch
                {
                    "1d" => await _dbService.CleanupByDaysAsync(1),
                    "2d" => await _dbService.CleanupByDaysAsync(2),
                    "3d" => await _dbService.CleanupByDaysAsync(3),
                    "7d" or "1w" => await _dbService.CleanupByDaysAsync(7),
                    "1m" => await _dbService.CleanupByTimeAsync(1, "mois"),
                    "3m" => await _dbService.CleanupByTimeAsync(3, "mois"),
                    "ReadOnly" => await _dbService.DeleteReadItemsAsync(),
                    "Archive6m" => await _dbService.CleanupArchivesAsync(6),
                    "All" or "full_cache" => await _dbService.ClearAllCacheAsync(keepSaved: true),
                    _ => 0
                };
                await UiHelper.AlertAsync("Nettoyage", $"{n} éléments supprimés.");
            });
        }

        private static string DescribeMode(string mode) => mode switch
        {
            "1d" => "1 jour", "2d" => "2 jours", "3d" => "3 jours", "7d" or "1w" => "7 jours",
            "1m" => "1 mois", "3m" => "3 mois", _ => mode
        };

        private async Task ClearAllArticlesAsync() => await ExecuteAdvancedCleanup("All");

        /// <summary>Conservé : réinitialisation complète.</summary>
        private async Task ExecuteDeleteDbCommand()
        {
            if (IsBusy) return;
            bool confirm = await UiHelper.ConfirmAsync("DANGER",
                "Supprimer TOUTES les bases (catégories, sources, articles ET archives) ?\nLes sources par défaut seront recréées.\n\nConseil : créez d'abord une sauvegarde.",
                "OUI, TOUT SUPPRIMER", "Annuler");
            if (!confirm) return;

            await RunAsync("Réinitialisation", async () =>
            {
                await _dbService.DeleteDatabaseFileAsync();
                await UiHelper.AlertAsync("Réinitialisé", "Toutes les bases ont été supprimées puis recréées.");
            });
        }
    }
}
