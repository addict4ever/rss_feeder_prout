using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Diagnostics;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Rss_feeder_prout.Models;
using Rss_feeder_prout.Services;
using Rss_feeder_prout.Views;

namespace Rss_feeder_prout.ViewModels
{
    /// <summary>Archives permanentes : recherche, note personnelle, export HTML.</summary>
    public class ArchiveViewModel : BaseViewModel
    {
        private readonly SQLiteService _database;
        private readonly BackupService _backup;
        private CancellationTokenSource? _debounce;

        public ObservableCollection<ArchiveItem> ArchivedItems { get; } = new();

        public ICommand LoadArchivesCommand { get; }
        public ICommand OpenArchiveCommand { get; }
        public ICommand DeleteArchiveCommand { get; }
        public ICommand ArchiveMenuCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand RefreshCommand { get; }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value ?? string.Empty))
                    Debounce();
            }
        }

        private bool _isRefreshing;
        public bool IsRefreshing
        {
            get => _isRefreshing;
            set => SetProperty(ref _isRefreshing, value);
        }

        private string _countText = string.Empty;
        public string CountText
        {
            get => _countText;
            set => SetProperty(ref _countText, value);
        }

        public ArchiveViewModel(SQLiteService database, BackupService backup)
        {
            _database = database;
            _backup = backup;
            Title = "Archives";

            LoadArchivesCommand = new Command(async () => await ExecuteLoadArchivesCommand());
            OpenArchiveCommand = new Command<ArchiveItem>(async (item) => await ExecuteOpenArchiveCommand(item));
            DeleteArchiveCommand = new Command<ArchiveItem>(async (item) => await ExecuteDeleteArchiveCommand(item));
            ArchiveMenuCommand = new Command<ArchiveItem>(async (item) => await ArchiveMenuAsync(item));
            ExportCommand = new Command(async () => await ExportAsync());
            RefreshCommand = new Command(async () => { IsRefreshing = true; await ExecuteLoadArchivesCommand(); IsRefreshing = false; });
        }

        private void Debounce()
        {
            _debounce?.Cancel();
            var cts = new CancellationTokenSource();
            _debounce = cts;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(350, cts.Token);
                    await MainThread.InvokeOnMainThreadAsync(ExecuteLoadArchivesCommand);
                }
                catch (OperationCanceledException) { }
            });
        }

        public async Task ExecuteLoadArchivesCommand()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                var items = await _database.GetArchivesAsync(SearchText);
                ArchivedItems.Clear();
                foreach (var item in items) ArchivedItems.Add(item);
                CountText = items.Count == 1 ? "1 article archivé" : $"{items.Count} articles archivés";
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

        async Task ExecuteOpenArchiveCommand(ArchiveItem? item)
        {
            if (item == null) return;
            // IsArchive=true indique au lecteur d'utiliser la base d'archives
            await UiHelper.GoToAsync($"{nameof(ArticleDetailPage)}?itemId={item.Id}&IsArchive=true");
        }

        async Task ExecuteDeleteArchiveCommand(ArchiveItem? item)
        {
            if (item == null) return;

            bool confirm = await UiHelper.ConfirmAsync("Supprimer l'archive",
                "Voulez-vous vraiment supprimer cet article de vos archives ?", "Supprimer", "Annuler");
            if (!confirm) return;

            try
            {
                await _database.DeleteArchiveAsync(item);
                ArchivedItems.Remove(item);
                CountText = $"{ArchivedItems.Count} articles archivés";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ARCHIVE] Erreur lors de la suppression : {ex.Message}");
                await UiHelper.AlertAsync("Erreur", "Impossible de supprimer l'archive.");
            }
        }

        private async Task ArchiveMenuAsync(ArchiveItem? item)
        {
            if (item == null) return;
            var choice = await UiHelper.ActionSheetAsync(Helpers.HtmlHelper.Truncate(item.Title, 60), "Annuler", "Supprimer",
                "Lire", item.HasNote ? "Modifier la note" : "Ajouter une note", "Ouvrir l'original", "Partager le lien");
            switch (choice)
            {
                case "Lire": await ExecuteOpenArchiveCommand(item); break;
                case "Ajouter une note":
                case "Modifier la note":
                    var note = await UiHelper.PromptAsync("Note personnelle", "Votre note sur cet article :", item.Note, "Ex. : à relire pour le projet…", maxLength: 500);
                    if (note != null)
                    {
                        item.Note = note.Trim();
                        var full = await _database.GetArchiveByIdAsync(item.Id);
                        if (full != null)
                        {
                            full.Note = item.Note;
                            await _database.UpdateArchiveAsync(full);
                        }
                    }
                    break;
                case "Ouvrir l'original": await UiHelper.OpenBrowserAsync(item.Link); break;
                case "Partager le lien":
                    if (!string.IsNullOrWhiteSpace(item.Link))
                        await Share.Default.RequestAsync(new ShareTextRequest { Uri = item.Link, Title = item.Title });
                    break;
                case "Supprimer": await ExecuteDeleteArchiveCommand(item); break;
            }
        }

        private async Task ExportAsync()
        {
            try
            {
                IsBusy = true;
                var all = await _database.GetArchivesFullAsync();
                if (all.Count == 0) { UiHelper.Toast("Aucune archive"); return; }
                var path = await _backup.ExportHtmlAsync("Mes archives",
                    all.Select(a => (a.Title, a.SiteName, a.ArchivedAt, a.Link,
                        (string.IsNullOrWhiteSpace(a.Note) ? string.Empty : $"<blockquote>📝 {Helpers.HtmlHelper.Encode(a.Note)}</blockquote>")
                        + (string.IsNullOrWhiteSpace(a.ContentHtml) ? $"<p>{Helpers.HtmlHelper.Encode(a.Summary)}</p>" : a.ContentHtml))));
                await BackupService.ShareFileAsync(path, "Archives Rss Prout");
            }
            catch (Exception ex)
            {
                await UiHelper.AlertAsync("Erreur", ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
