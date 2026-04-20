using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Diagnostics;
using Rss_feeder_prout.Models;
using Rss_feeder_prout.Services;
using Rss_feeder_prout.Views;

namespace Rss_feeder_prout.ViewModels
{
    public class ArchiveViewModel : BaseViewModel
    {
        private readonly SQLiteService _database;
        public ObservableCollection<ArchiveItem> ArchivedItems { get; } = new();

        public ICommand LoadArchivesCommand { get; }
        public ICommand OpenArchiveCommand { get; }
        public ICommand DeleteArchiveCommand { get; }

        public ArchiveViewModel(SQLiteService database)
        {
            _database = database;

            Title = "Articles Archivés";

            // Initialisation des commandes
            LoadArchivesCommand = new Command(async () => await ExecuteLoadArchivesCommand());
            OpenArchiveCommand = new Command<ArchiveItem>(async (item) => await ExecuteOpenArchiveCommand(item));
            DeleteArchiveCommand = new Command<ArchiveItem>(async (item) => await ExecuteDeleteArchiveCommand(item));
        }

        async Task ExecuteLoadArchivesCommand()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                ArchivedItems.Clear();
                // Récupération des archives depuis la base de données
                var items = await _database.GetArchivesAsync();

                foreach (var item in items)
                {
                    // L'objet item contient déjà FaviconUrl et le contenu téléchargé
                    ArchivedItems.Add(item);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ARCHIVE] Erreur de chargement : {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        async Task ExecuteOpenArchiveCommand(ArchiveItem item)
        {
            if (item == null) return;

            try
            {
                // Navigation vers le détail avec itemId et le flag IsArchive
                await Shell.Current.GoToAsync($"{nameof(ArticleDetailPage)}?itemId={item.Id}&IsArchive=true");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ARCHIVE] Erreur de navigation : {ex.Message}");
            }
        }

        async Task ExecuteDeleteArchiveCommand(ArchiveItem item)
        {
            if (item == null) return;

            // Demande de confirmation avant suppression
            bool confirm = await Shell.Current.DisplayAlert(
                "Supprimer l'archive", 
                "Voulez-vous vraiment supprimer cet article de vos archives ?", 
                "Supprimer", 
                "Annuler");

            if (confirm)
            {
                try
                {
                    // Suppression physique dans la DB
                    // On envoie l'objet 'item' directement, pas 'item.Id'
                    await _database.DeleteArchiveAsync(item);

                    // Suppression visuelle dans la liste
                    ArchivedItems.Remove(item);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ARCHIVE] Erreur lors de la suppression : {ex.Message}");
                    await Shell.Current.DisplayAlert("Erreur", "Impossible de supprimer l'archive.", "OK");
                }
            }
        }
    }
}