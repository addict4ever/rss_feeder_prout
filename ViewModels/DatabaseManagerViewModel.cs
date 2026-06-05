using Microsoft.Maui.Controls;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Maui.Storage;
using Rss_feeder_prout.Services;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.IO.Compression;

namespace Rss_feeder_prout.ViewModels
{
    public class DatabaseManagerViewModel : BaseViewModel
    {
        private readonly SQLiteService _dbService;

        // Liste des fichiers DB affichée dans le XAML
        public ObservableCollection<string> DatabaseFiles { get; } = new ObservableCollection<string>();

        // Commandes
        public ICommand ExportDbCommand { get; }
        public ICommand DeleteDbCommand { get; }
        public ICommand AdvancedCleanupCommand { get; }
        public ICommand RefreshDbListCommand { get; }
        public ICommand ImportDbCommand { get; }

        public bool IsNotBusy => !IsBusy;

        public DatabaseManagerViewModel(SQLiteService dbService)
        {
            _dbService = dbService;

            // Initialisation des commandes
            ExportDbCommand = new Command(async () => await ExecuteExportDbCommand());
            DeleteDbCommand = new Command(async () => await ExecuteDeleteDbCommand());
            AdvancedCleanupCommand = new Command<string>(async (mode) => await ExecuteAdvancedCleanup(mode));
            RefreshDbListCommand = new Command(ExecuteRefreshDbList);
            ImportDbCommand = new Command(async () => await ExecuteImportDbCommand());

            // Charger la liste automatiquement à l'ouverture
            ExecuteRefreshDbList();
        }

        /// <summary>
        /// Scanne le dossier de l'application pour lister tous les fichiers .db3
        /// </summary>
        private void ExecuteRefreshDbList()
        {
            try
            {
                DatabaseFiles.Clear();

                // Racine du package (ex: /data/user/0/com.company.app/)
                string appRoot = Directory.GetParent(FileSystem.AppDataDirectory).FullName;

                var pathsToScan = new List<string>
        {
            FileSystem.AppDataDirectory,                      // Le dossier /files
            appRoot,                                          // La racine du dossier app
            Path.Combine(appRoot, "databases"),               // 🎯 LE PLUS IMPORTANT : Le dossier spécial SQLite
            FileSystem.CacheDirectory                         // Le dossier /cache
        };

                var extensions = new[] { ".db", ".db3", ".sqlite", ".sqlite3" };

                foreach (var path in pathsToScan.Distinct())
                {
                    if (!Directory.Exists(path)) continue;

                    // On récupère les fichiers
                    var files = Directory.EnumerateFiles(path);

                    foreach (var filePath in files)
                    {
                        var info = new FileInfo(filePath);
                        string ext = Path.GetExtension(filePath).ToLower();

                        // On filtre : soit l'extension, soit le nom contient "Rss"
                        if (extensions.Contains(ext) || info.Name.Contains("Rss"))
                        {
                            // On nettoie le nom du dossier pour l'affichage (ex: "databases" ou "files")
                            string folderName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
                            if (string.IsNullOrEmpty(folderName)) folderName = "Root";

                            string display = $"{info.Name} ({info.Length / 1024} Ko) [{folderName}]";

                            // Éviter les doublons si un fichier apparaît dans deux listes
                            if (!DatabaseFiles.Contains(display))
                            {
                                DatabaseFiles.Add(display);
                            }
                        }
                    }
                }

                if (DatabaseFiles.Count == 0)
                {
                    Debug.WriteLine("Aucune base de données trouvée dans les dossiers scannés.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur listing DB: {ex.Message}");
            }
        }

        /// <summary>
        /// Logique de nettoyage par suppression de fichiers physiques (Bases horodatées)
        /// </summary>
        private async Task ExecuteAdvancedCleanup(string mode)
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                int deletedFilesCount = 0;
                // On récupère uniquement les bases d'articles (RssProut_...)
                var files = Directory.GetFiles(FileSystem.AppDataDirectory, "RssProut_*.db3");
                DateTime limitDate = DateTime.Now;

                // Déterminer la date limite selon le mode
                switch (mode)
                {
                    case "1d": limitDate = DateTime.Now.AddDays(-1); break;
                    case "1w": limitDate = DateTime.Now.AddDays(-7); break;
                    case "1m": limitDate = DateTime.Now.AddMonths(-1); break;
                    case "full_cache": limitDate = DateTime.Now.AddDays(1); break; // Futur pour tout inclure
                }

                foreach (var filePath in files)
                {
                    string fileName = Path.GetFileName(filePath);

                    // Extraire la date du nom de fichier : RssProut_YYYY_MM_DD.db3
                    var datePart = fileName.Replace("RssProut_", "").Replace(".db3", "");
                    var parts = datePart.Split('_');

                    if (parts.Length == 3 &&
                        int.TryParse(parts[0], out int year) &&
                        int.TryParse(parts[1], out int month) &&
                        int.TryParse(parts[2], out int day))
                    {
                        DateTime fileDate = new DateTime(year, month, day);

                        // Si le fichier est plus vieux que la limite
                        if (fileDate.Date < limitDate.Date || mode == "full_cache")
                        {
                            // Protection : On ne supprime pas le fichier d'aujourd'hui sauf en full_cache
                            if (fileDate.Date != DateTime.Now.Date || mode == "full_cache")
                            {
                                // On tente de fermer la connexion si nécessaire via le service (optionnel)
                                File.Delete(filePath);
                                deletedFilesCount++;
                            }
                        }
                    }
                }

                ExecuteRefreshDbList();
                await Shell.Current.DisplayAlert("Nettoyage", $"{deletedFilesCount} fichiers de base de données supprimés.", "OK");
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Erreur", $"Erreur lors du nettoyage : {ex.Message}", "OK");
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Importation d'un fichier .db3 externe
        /// </summary>
        private async Task ExecuteImportDbCommand()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                var result = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "Sélectionnez une base de données (.db3)",
                });

                if (result == null) return;

                if (!result.FileName.EndsWith(".db3", StringComparison.OrdinalIgnoreCase))
                {
                    await Shell.Current.DisplayAlert("Format incorrect", "Le fichier doit être un .db3", "OK");
                    return;
                }

                string targetPath = Path.Combine(FileSystem.AppDataDirectory, result.FileName);

                using (var stream = await result.OpenReadAsync())
                using (var newFile = File.Create(targetPath))
                {
                    await stream.CopyToAsync(newFile);
                }

                await Shell.Current.DisplayAlert("Succès", $"Fichier {result.FileName} importé.", "OK");
                ExecuteRefreshDbList();
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Erreur Import", ex.Message, "OK");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ExecuteExportDbCommand()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                // 1. Menu de sélection
                string action = await Shell.Current.DisplayActionSheet(
                    "Quel export choisir ?",
                    "Annuler",
                    null,
                    "Fichier de Configuration (Sites/Playlists)",
                    "Articles d'aujourd'hui",
                    "Toutes les bases de données (ZIP)");

                if (action == "Annuler" || string.IsNullOrEmpty(action)) return;

                string finalExportPath = "";
                string exportName = "";
                string appDataPath = FileSystem.AppDataDirectory;

                // --- CAS 1 : TOUTES LES BASES (ZIP) ---
                if (action == "Toutes les bases de données (ZIP)")
                {
                    exportName = $"Backup_Total_{DateTime.Now:yyyy_MM_dd}.zip";
                    finalExportPath = Path.Combine(FileSystem.CacheDirectory, exportName);

                    if (File.Exists(finalExportPath)) File.Delete(finalExportPath);

                    // On récupère tous les fichiers .db3
                    var allDbFiles = Directory.GetFiles(appDataPath, "*.db3");

                    if (allDbFiles.Length == 0)
                    {
                        await Shell.Current.DisplayAlert("Info", "Aucun fichier .db3 trouvé.", "OK");
                        return;
                    }

                    using (var zip = ZipFile.Open(finalExportPath, ZipArchiveMode.Create))
                    {
                        foreach (var filePath in allDbFiles)
                        {
                            zip.CreateEntryFromFile(filePath, Path.GetFileName(filePath));
                        }
                    }
                }
                // --- CAS 2 & 3 : FICHIER UNIQUE ---
                else
                {
                    string sourceFileName = "";
                    if (action == "Fichier de Configuration (Sites/Playlists)")
                    {
                        sourceFileName = "RssConfig.db3";
                        exportName = "RssConfig_Export.db3";
                    }
                    else // Articles d'aujourd'hui
                    {
                        sourceFileName = $"RssProut_{DateTime.Now:yyyy_MM_dd}.db3";
                        exportName = $"RssArticles_Aujourdhui_{DateTime.Now:yyyy_MM_dd}.db3";
                    }

                    string sourcePath = Path.Combine(appDataPath, sourceFileName);

                    if (!File.Exists(sourcePath))
                    {
                        await Shell.Current.DisplayAlert("Erreur", $"Fichier {sourceFileName} introuvable.", "OK");
                        return;
                    }

                    finalExportPath = Path.Combine(FileSystem.CacheDirectory, exportName);
                    File.Copy(sourcePath, finalExportPath, overwrite: true);
                }

                // 2. Partage du fichier (ZIP ou DB)
                await Share.RequestAsync(new ShareFileRequest
                {
                    Title = "Export des bases de données RSS",
                    File = new ShareFile(finalExportPath)
                });
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Erreur", $"Erreur lors de l'export : {ex.Message}", "OK");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ExecuteDeleteDbCommand()
        {
            if (IsBusy) return;
            bool confirm = await Shell.Current.DisplayAlert("DANGER", "Supprimer TOUTES les bases de données (Config + Articles) ?", "OUI TOUT", "Annuler");

            if (confirm)
            {
                IsBusy = true;
                try
                {
                    // Appelle la méthode massive du service qui ferme les connexions et delete les fichiers
                    await _dbService.DeleteDatabaseFileAsync();
                    ExecuteRefreshDbList();
                    await Shell.Current.DisplayAlert("Réinitialisé", "Toutes les bases ont été supprimées.", "OK");
                }
                catch (Exception ex) { await Shell.Current.DisplayAlert("Erreur", ex.Message, "OK"); }
                finally { IsBusy = false; }
            }
        }
    }
}