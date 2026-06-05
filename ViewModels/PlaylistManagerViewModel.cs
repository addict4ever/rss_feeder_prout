using Rss_feeder_prout.Models;
using Rss_feeder_prout.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using System;
using Rss_feeder_prout.Views;

namespace Rss_feeder_prout.ViewModels
{
    public class PlaylistManagerViewModel : BaseViewModel
    {
        private readonly SQLiteService _dbService;

        // Collection principale (Source de vérité)
        public ObservableCollection<FeedPlaylist> Playlists { get; } = new ObservableCollection<FeedPlaylist>();

        // Collection filtrée (Celle liée à la CollectionView dans le XAML)
        private ObservableCollection<FeedPlaylist> _filteredPlaylists;
        public ObservableCollection<FeedPlaylist> FilteredPlaylists
        {
            get => _filteredPlaylists;
            set => SetProperty(ref _filteredPlaylists, value);
        }

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    FilterPlaylists();
                }
            }
        }

        // Commandes
        public ICommand LoadPlaylistsCommand { get; }
        public ICommand AddPlaylistCommand { get; }
        public ICommand DeletePlaylistCommand { get; }
        public ICommand EditPlaylistCommand { get; }
        public ICommand SyncAllCommand { get; }

        public PlaylistManagerViewModel(SQLiteService dbService)
        {
            _dbService = dbService;

            // Initialisation de la collection filtrée
            FilteredPlaylists = new ObservableCollection<FeedPlaylist>();

            // Initialisation des commandes
            LoadPlaylistsCommand = new Command(async () => await ExecuteLoadPlaylistsCommand());
            AddPlaylistCommand = new Command(async () => await ExecuteAddPlaylistCommand());
            DeletePlaylistCommand = new Command<FeedPlaylist>(async (p) => await ExecuteDeletePlaylistCommand(p));
            EditPlaylistCommand = new Command<FeedPlaylist>(async (p) => await ExecuteEditPlaylistCommand(p));
            SyncAllCommand = new Command(async () => await ExecuteSyncAllCommand());

            // Chargement initial des données
            Task.Run(async () => await ExecuteLoadPlaylistsCommand());
        }

        // --- Méthode de Chargement ---
        public async Task ExecuteLoadPlaylistsCommand()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                // On récupère les playlists depuis la base de configuration
                var list = await _dbService.GetPlaylistsAsync();

                // On met à jour la collection sur le thread principal pour l'UI
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Playlists.Clear();
                    foreach (var p in list)
                    {
                        Playlists.Add(p);
                    }
                    FilterPlaylists();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] Failed to load playlists: {ex.Message}");
                await Shell.Current.DisplayAlert("Erreur", "Échec du chargement des playlists.", "OK");
            }
            finally
            {
                IsBusy = false;
            }
        }

        // --- Logique de Filtrage ---
        private void FilterPlaylists()
        {
            // On travaille sur le thread principal pour modifier FilteredPlaylists
            MainThread.BeginInvokeOnMainThread(() =>
            {
                FilteredPlaylists.Clear();

                if (string.IsNullOrWhiteSpace(SearchText))
                {
                    foreach (var p in Playlists)
                    {
                        FilteredPlaylists.Add(p);
                    }
                }
                else
                {
                    var lowerSearchText = SearchText.Trim().ToLowerInvariant();
                    var results = Playlists.Where(p => p.Name != null && p.Name.ToLowerInvariant().Contains(lowerSearchText)).ToList();

                    foreach (var p in results)
                    {
                        FilteredPlaylists.Add(p);
                    }
                }
            });
        }

        // --- Commande d'Ajout : Création + Navigation ---
        private async Task ExecuteAddPlaylistCommand()
        {
            string name = await Shell.Current.DisplayPromptAsync("Nouvelle Playlist", "Nom de la playlist :", "OK", "Annuler");

            if (!string.IsNullOrWhiteSpace(name))
            {
                var newPlaylist = new FeedPlaylist
                {
                    Name = name.Trim(),
                    IsActive = true
                };

                // 1. Sauvegarder dans la DB de config pour obtenir un ID
                await _dbService.SavePlaylistAsync(newPlaylist);

                // 2. Ajouter localement pour l'affichage
                Playlists.Add(newPlaylist);
                FilterPlaylists();

                // 3. Naviguer vers la page de détail pour ajouter des sites RSS à cette playlist
                await Shell.Current.GoToAsync($"{nameof(PlaylistDetailPage)}?id={newPlaylist.Id}");
            }
        }

        // --- Commande de Suppression ---
        private async Task ExecuteDeletePlaylistCommand(FeedPlaylist playlist)
        {
            if (playlist == null) return;

            bool confirm = await Shell.Current.DisplayAlert("Confirmer la suppression",
                $"Voulez-vous vraiment supprimer la playlist '{playlist.Name}' ?\n\nCela supprimera les sites liés (Config) et les articles téléchargés aujourd'hui (Daily DB).",
                "Oui", "Non");

            if (confirm)
            {
                IsBusy = true;
                try
                {
                    // Suppression asynchrone gérant les deux bases de données
                    await _dbService.DeletePlaylistAsync(playlist);

                    // Mise à jour de l'UI
                    Playlists.Remove(playlist);
                    FilterPlaylists();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ERROR] DeletePlaylist: {ex.Message}");
                    await Shell.Current.DisplayAlert("Erreur", "Impossible de supprimer la playlist.", "OK");
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        // --- Commande d'Édition ---
        private async Task ExecuteEditPlaylistCommand(FeedPlaylist playlist)
        {
            if (playlist == null) return;

            // Navigation vers la page de détail/édition avec l'ID en paramètre
            await Shell.Current.GoToAsync($"{nameof(PlaylistDetailPage)}?id={playlist.Id}");
        }

        // --- Commande de Synchronisation Globale ---
        private async Task ExecuteSyncAllCommand()
        {
            if (IsBusy) return;

            IsBusy = true;
            try
            {
                // Note: Ici vous pourriez appeler votre RssService pour boucler sur les playlists
                await Shell.Current.DisplayAlert("Synchronisation", "Mise à jour des flux lancée...", "OK");

                // Exemple d'appel futur :
                // await _rssService.SyncAllActivePlaylistsAsync();

                // On rafraîchit la vue après synchro
                await ExecuteLoadPlaylistsCommand();
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Erreur de Synchro", ex.Message, "OK");
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}