// File: ViewModels/DatabaseManagerViewModel.cs

using Microsoft.Maui.Controls;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Maui.Storage; // Fournit FileSystem et Share
using Rss_feeder_prout.Services;
using System;

namespace Rss_feeder_prout.ViewModels
{
    // La classe est PUBLIC (essentiel) et hérite de BaseViewModel
    public class DatabaseManagerViewModel : BaseViewModel
    {
        private readonly SQLiteService _dbService;

        public ICommand ExportDbCommand { get; }
        public ICommand DeleteDbCommand { get; }

        public ICommand CleanupCommand { get; }
        public ICommand ClearAllCacheCommand { get; }

        public ICommand AdvancedCleanupCommand { get; }

        // Propriété calculée pour le XAML
        public bool IsNotBusy => !IsBusy;

        public DatabaseManagerViewModel(SQLiteService dbService)
        {
            _dbService = dbService;
            ExportDbCommand = new Command(async () => await ExecuteExportDbCommand());
            DeleteDbCommand = new Command(async () => await ExecuteDeleteDbCommand());
            CleanupCommand = new Command<string>(async (days) => await ExecuteCleanupCommand(days));
            ClearAllCacheCommand = new Command(async () => await ExecuteClearAllCacheCommand());
            AdvancedCleanupCommand = new Command<string>(async (mode) => await ExecuteAdvancedCleanup(mode));

        }

        private async Task ExecuteAdvancedCleanup(string mode)
        {
            if (IsBusy) return; 
            IsBusy = true;

                    try
                    {
                        int deleted = 0;
                        string message = "";

                        switch (mode)
                        {
                            case "1d": deleted = await _dbService.CleanupByTimeAsync(1, "jour"); break;
                            case "1w": deleted = await _dbService.CleanupByTimeAsync(7, "jour"); break;
                            case "1m":
                                deleted = await _dbService.CleanupByTimeAsync(1, "mois"); break;
                                case "read": deleted = await _dbService.DeleteReadItemsAsync(); break; 
                    case "archive_6m": deleted = await _dbService.CleanupArchivesAsync(6); break;
                                case "full_cache": deleted = await _dbService.ClearTableAsync("RssItem"); break;
                                }

                                await Shell.Current.DisplayAlert("Nettoyage", $"{deleted} éléments supprimés.", "OK"); 
            }
            catch (Exception ex)
                    {
                        await Shell.Current.DisplayAlert("Erreur", ex.Message, "OK"); 
            }
                    finally { IsBusy = false; }
            
        }

        private async Task ExecuteCleanupCommand(string daysParam)
        {
            if (IsBusy || !int.TryParse(daysParam, out int days)) return;

            IsBusy = true;
            try
            {
                int deletedCount = await _dbService.CleanupByDaysAsync(days);
                string message = days == 0 ? "Articles lus supprimés." : $"{deletedCount} articles de plus de {days} jours supprimés.";
                await Shell.Current.DisplayAlert("Nettoyage", message, "OK");
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Erreur", ex.Message, "OK");
            }
            finally { IsBusy = false; }
        }

        private async Task ExecuteClearAllCacheCommand()
        {
            bool confirm = await Shell.Current.DisplayAlert("Confirmation", "Vider tout le cache des articles ? (Vos archives resteront intactes)", "Oui", "Non");
            if (!confirm) return;

            IsBusy = true;
            await _dbService.ClearAllCacheAsync();
            IsBusy = false;
            await Shell.Current.DisplayAlert("Succès", "Le cache a été vidé.", "OK");
        }

        private async Task ExecuteExportDbCommand()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                string sourcePath = _dbService.GetDatabasePath();

                if (!File.Exists(sourcePath))
                {
                    await Shell.Current.DisplayAlert("Erreur d'Exportation", "Aucun fichier de base de données trouvé à exporter.", "OK");
                    return;
                }

                // 1. Créer une copie temporaire dans le répertoire Cache de l'application
                //    Ceci est nécessaire car l'API Share/Partage ne peut pas toujours accéder directement au dossier AppData.
                string tempFilePath = Path.Combine(FileSystem.CacheDirectory, "RssProutDB_export.db3");

                // Copier le fichier DB vers l'emplacement temporaire
                File.Copy(sourcePath, tempFilePath, overwrite: true);

                // 2. Utiliser l'API Share pour présenter le fichier
                await Share.RequestAsync(new ShareFileRequest
                {
                    Title = "Exporter la Base de Données RSS",
                    File = new ShareFile(tempFilePath)
                });

                // 3. Informer l'utilisateur (Partage est asynchrone et non bloquant)
                await Shell.Current.DisplayAlert(
                    "Partage Ouvert",
                    "Veuillez choisir une application (ex: Enregistrer dans Fichiers ou Drive) pour sauvegarder la base de données.",
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error exporting DB with Share: {ex.Message}");
                await Shell.Current.DisplayAlert("Erreur", "Échec de l'exportation ou du partage du fichier.", "OK");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ExecuteDeleteDbCommand()
        {
            if (IsBusy) return;

            bool confirm = await Shell.Current.DisplayAlert(
                "ATTENTION",
                "Êtes-vous sûr de vouloir SUPPRIMER la base de données ? Toutes les playlists et articles en cache seront perdus.",
                "Oui, supprimer",
                "Annuler");

            if (confirm)
            {
                IsBusy = true;
                try
                {
                    await _dbService.DeleteDatabaseFileAsync();

                    await Shell.Current.DisplayAlert(
                        "Suppression Réussie",
                        "La base de données a été supprimée. L'application devra recharger pour utiliser une nouvelle DB.",
                        "OK");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error deleting DB: {ex.Message}");
                    await Shell.Current.DisplayAlert("Erreur", $"Échec de la suppression de la DB: {ex.Message}", "OK");
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }
    }
}