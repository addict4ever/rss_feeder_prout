using Rss_feeder_prout.Models;
using Rss_feeder_prout.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Diagnostics;
using Rss_feeder_prout.Views;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace Rss_feeder_prout.ViewModels
{
    /// <summary>Gestion des catégories : ajout, renommage, couleur, ordre, synchro on/off, import / export OPML.</summary>
    public class PlaylistManagerViewModel : BaseViewModel
    {
        private readonly SQLiteService _dbService;
        private readonly BackupService _backup;
        private readonly SyncService _sync;

        // Collection principale (Source de vérité)
        public ObservableCollection<FeedPlaylist> Playlists { get; } = new ObservableCollection<FeedPlaylist>();

        // Collection filtrée (Celle liée à la CollectionView dans le XAML)
        public ObservableCollection<FeedPlaylist> FilteredPlaylists { get; } = new ObservableCollection<FeedPlaylist>();

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value ?? string.Empty))
                    FilterPlaylists();
            }
        }

        public ICommand LoadPlaylistsCommand { get; }
        public ICommand AddPlaylistCommand { get; }
        public ICommand DeletePlaylistCommand { get; }
        public ICommand EditPlaylistCommand { get; }
        public ICommand PlaylistMenuCommand { get; }
        public ICommand SyncAllCommand { get; }
        public ICommand ImportOpmlCommand { get; }
        public ICommand ExportOpmlCommand { get; }

        public PlaylistManagerViewModel(SQLiteService dbService, BackupService backup, SyncService sync)
        {
            _dbService = dbService;
            _backup = backup;
            _sync = sync;
            Title = "Catégories & sources";

            LoadPlaylistsCommand = new Command(async () => await ExecuteLoadPlaylistsCommand());
            AddPlaylistCommand = new Command(async () => await ExecuteAddPlaylistCommand());
            DeletePlaylistCommand = new Command<FeedPlaylist>(async (p) => await ExecuteDeletePlaylistCommand(p));
            EditPlaylistCommand = new Command<FeedPlaylist>(async (p) => await ExecuteEditPlaylistCommand(p));
            PlaylistMenuCommand = new Command<FeedPlaylist>(async (p) => await PlaylistMenuAsync(p));
            SyncAllCommand = new Command(async () => await ExecuteSyncAllCommand());
            ImportOpmlCommand = new Command(async () => await ImportOpmlAsync());
            ExportOpmlCommand = new Command(async () => await ExportOpmlAsync());
        }

        public async Task ExecuteLoadPlaylistsCommand()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                var list = await _dbService.GetPlaylistsAsync();
                var siteCounts = await _dbService.GetSiteCountsByPlaylistAsync();
                var unread = await _dbService.GetUnreadCountsByPlaylistAsync();
                foreach (var p in list)
                {
                    p.SiteCount = siteCounts.TryGetValue(p.Id, out var c) ? c : 0;
                    p.UnreadCount = unread.TryGetValue(p.Id, out var u) ? u : 0;
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Playlists.Clear();
                    foreach (var p in list) Playlists.Add(p);
                    FilterPlaylists();
                    Subtitle = $"{list.Count} catégories · {siteCounts.Values.Sum()} sources";
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] Failed to load playlists: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void FilterPlaylists()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                FilteredPlaylists.Clear();
                var q = SearchText.Trim().ToLowerInvariant();
                foreach (var p in Playlists.Where(p => string.IsNullOrEmpty(q) || (p.Name ?? string.Empty).ToLowerInvariant().Contains(q)))
                    FilteredPlaylists.Add(p);
            });
        }

        private async Task ExecuteAddPlaylistCommand()
        {
            string? name = await UiHelper.PromptAsync("Nouvelle catégorie", "Nom de la catégorie :", placeholder: "Ex. : Actualités Tech");
            if (string.IsNullOrWhiteSpace(name)) return;

            var newPlaylist = new FeedPlaylist
            {
                Name = name.Trim(),
                IsActive = true,
                ColorHex = FeedPlaylist.ColorChoices[Playlists.Count % FeedPlaylist.ColorChoices.Length]
            };

            await _dbService.SavePlaylistAsync(newPlaylist);
            Playlists.Add(newPlaylist);
            FilterPlaylists();

            // Ouvre directement la page pour ajouter des sources
            await UiHelper.GoToAsync($"{nameof(PlaylistDetailPage)}?id={newPlaylist.Id}");
        }

        private async Task ExecuteDeletePlaylistCommand(FeedPlaylist? playlist)
        {
            if (playlist == null) return;

            bool confirm = await UiHelper.ConfirmAsync("Supprimer la catégorie",
                $"Supprimer « {playlist.Name} », ses {playlist.SiteCount} sources et TOUS leurs articles ?\n\nLes archives sont conservées.",
                "Supprimer", "Annuler");
            if (!confirm) return;

            IsBusy = true;
            try
            {
                await _dbService.DeletePlaylistAsync(playlist);
                Playlists.Remove(playlist);
                FilterPlaylists();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] DeletePlaylist: {ex.Message}");
                await UiHelper.AlertAsync("Erreur", "Impossible de supprimer la catégorie.");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ExecuteEditPlaylistCommand(FeedPlaylist? playlist)
        {
            if (playlist == null) return;
            await UiHelper.GoToAsync($"{nameof(PlaylistDetailPage)}?id={playlist.Id}");
        }

        private async Task PlaylistMenuAsync(FeedPlaylist? p)
        {
            if (p == null) return;
            var choice = await UiHelper.ActionSheetAsync(p.Name, "Annuler", "Supprimer",
                "Gérer les sources", "Renommer", "Couleur", p.IsActive ? "Mettre la synchro en pause" : "Réactiver la synchro",
                "Monter", "Descendre");

            switch (choice)
            {
                case "Gérer les sources": await ExecuteEditPlaylistCommand(p); break;
                case "Renommer":
                    var name = await UiHelper.PromptAsync("Renommer", "Nouveau nom :", p.Name);
                    if (!string.IsNullOrWhiteSpace(name)) { p.Name = name.Trim(); await _dbService.SavePlaylistAsync(p); }
                    break;
                case "Couleur":
                    var color = await UiHelper.ActionSheetAsync("Couleur", "Annuler", null, FeedPlaylist.ColorNames);
                    var idx = Array.IndexOf(FeedPlaylist.ColorNames, color ?? string.Empty);
                    if (idx >= 0) { p.ColorHex = FeedPlaylist.ColorChoices[idx]; await _dbService.SavePlaylistAsync(p); }
                    break;
                case "Mettre la synchro en pause":
                case "Réactiver la synchro":
                    p.IsActive = !p.IsActive;
                    await _dbService.SavePlaylistAsync(p);
                    break;
                case "Monter":
                case "Descendre":
                    var i = Playlists.IndexOf(p);
                    var j = choice == "Monter" ? i - 1 : i + 1;
                    if (i >= 0 && j >= 0 && j < Playlists.Count)
                    {
                        Playlists.Move(i, j);
                        await _dbService.SavePlaylistOrderAsync(Playlists.ToList());
                        FilterPlaylists();
                    }
                    break;
                case "Supprimer": await ExecuteDeletePlaylistCommand(p); break;
            }
        }

        /// <summary>Synchronisation globale réelle (l'ancienne version n'affichait qu'un message).</summary>
        private async Task ExecuteSyncAllCommand()
        {
            if (_sync.IsRunning) { UiHelper.Toast("Une synchronisation est déjà en cours"); return; }
            var options = await SyncOptionsPage.AskAsync();
            if (options == null) return;
            _ = Task.Run(async () =>
            {
                var r = await _sync.RunAsync(options);
                UiHelper.Toast(r.Summary);
                await ExecuteLoadPlaylistsCommand();
            });
        }

        private async Task ImportOpmlAsync()
        {
            try
            {
                var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choisir un fichier OPML" });
                if (file == null) return;
                IsBusy = true;
                using var stream = await file.OpenReadAsync();
                using var reader = new StreamReader(stream);
                var content = await reader.ReadToEndAsync();
                var (p, s) = await _backup.ImportOpmlAsync(content);
                IsBusy = false;
                await UiHelper.AlertAsync("Import OPML", s == 0 ? "Aucune nouvelle source trouvée dans ce fichier." : $"{s} sources ajoutées ({p} nouvelles catégories).");
                await ExecuteLoadPlaylistsCommand();
            }
            catch (Exception ex)
            {
                await UiHelper.AlertAsync("Erreur", "Import impossible : " + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ExportOpmlAsync()
        {
            try
            {
                var path = await _backup.ExportOpmlAsync();
                await BackupService.ShareFileAsync(path, "Mes flux (OPML)");
            }
            catch (Exception ex)
            {
                await UiHelper.AlertAsync("Erreur", "Export impossible : " + ex.Message);
            }
        }
    }
}
