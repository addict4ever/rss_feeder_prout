using Rss_feeder_prout.Models;
using Rss_feeder_prout.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using System;
// 🎯 AJOUT: Importation de la nouvelle page pour la navigation
using Rss_feeder_prout.Views;

namespace Rss_feeder_prout.ViewModels
{
    public class PlaylistManagerViewModel : BaseViewModel
    {
        private readonly SQLiteService _dbService;
        // private readonly RssParsingService _rssService; // Décommenter si le service est ajouté

        public ObservableCollection<FeedPlaylist> Playlists { get; } = new ObservableCollection<FeedPlaylist>();

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

        public ICommand LoadPlaylistsCommand { get; }
        public ICommand AddPlaylistCommand { get; }
        public ICommand DeletePlaylistCommand { get; }
        public ICommand EditPlaylistCommand { get; }
        public ICommand SyncAllCommand { get; }

        public PlaylistManagerViewModel(SQLiteService dbService /*, RssParsingService rssService*/)
        {
            _dbService = dbService;

            // Initialisation de la collection filtrée pour éviter les NullReferenceException
            _filteredPlaylists = new ObservableCollection<FeedPlaylist>();

            LoadPlaylistsCommand = new Command(async () => await ExecuteLoadPlaylistsCommand());
            // 🎯 MODIFICATION : AddPlaylistCommand navigue directement vers l'édition après le nom
            AddPlaylistCommand = new Command(async () => await ExecuteAddPlaylistCommand());
            DeletePlaylistCommand = new Command<FeedPlaylist>(async (p) => await ExecuteDeletePlaylistCommand(p));
            EditPlaylistCommand = new Command<FeedPlaylist>(async (p) => await ExecuteEditPlaylistCommand(p));
            SyncAllCommand = new Command(async () => await ExecuteSyncAllCommand());

            // Chargement initial des données (appelé sur Task.Run ou OnAppearing)
            Task.Run(ExecuteLoadPlaylistsCommand);
        }

        // --- Méthode de Chargement ---
        private async Task ExecuteLoadPlaylistsCommand()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                // Vider et recharger la liste principale
                Playlists.Clear();
                var list = await _dbService.GetPlaylistsAsync();
                foreach (var p in list)
                {
                    Playlists.Add(p);
                }

                // Appliquer le filtre ou afficher la liste complète
                FilterPlaylists();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] Failed to load playlists: {ex.Message}");
                // 🎯 AMÉLIORATION : Afficher une alerte utilisateur en cas d'échec
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
            // Efface et ajoute pour déclencher la notification UI de la CollectionView
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
                var results = Playlists.Where(p => p.Name.ToLowerInvariant().Contains(lowerSearchText)).ToList();

                foreach (var p in results)
                {
                    FilteredPlaylists.Add(p);
                }
            }
        }

        // --- 🎯 MODIFICATION : Commande d'Ajout (Navigue vers la page de Détail) ---
        private async Task ExecuteAddPlaylistCommand()
        {
            string name = await Shell.Current.DisplayPromptAsync("Nouvelle Playlist", "Nom de la playlist :", "OK", "Annuler");

            if (!string.IsNullOrWhiteSpace(name))
            {
                var newPlaylist = new FeedPlaylist { Name = name.Trim(), IsActive = true };

                // 1. Sauvegarder d'abord pour obtenir un ID
                await _dbService.SavePlaylistAsync(newPlaylist);

                // 2. Naviguer vers la page de détail pour ajouter les URLs
                await Shell.Current.GoToAsync($"PlaylistDetailPage?id={newPlaylist.Id}");

                // NOTE : Le LoadPlaylistsCommand dans le Manager sera exécuté quand on reviendra (via le OnAppearing)
            }
        }

        // --- Commande de Suppression (pas de changement majeur) ---
        private async Task ExecuteDeletePlaylistCommand(FeedPlaylist playlist)
        {
            if (playlist == null) return;

            bool confirm = await Shell.Current.DisplayAlert("Confirmer la suppression",
                                                           $"Voulez-vous vraiment supprimer la playlist '{playlist.Name}' et tous ses articles mis en cache ?",
                                                           "Oui", "Non");
            if (confirm)
            {
                await _dbService.DeletePlaylistAsync(playlist);

                // Suppression de la mémoire pour rafraîchissement immédiat
                Playlists.Remove(playlist);
                FilteredPlaylists.Remove(playlist);
            }
        }

        // --- Commande d'Édition/Détail (pas de changement majeur) ---
        private async Task ExecuteEditPlaylistCommand(FeedPlaylist playlist)
        {
            if (playlist == null) return;

            // Navigation vers la page de détail/édition
            await Shell.Current.GoToAsync($"{nameof(PlaylistDetailPage)}?id={playlist.Id}");
        }

        // --- Commande de Synchronisation Globale (pas de changement majeur) ---
        private async Task ExecuteSyncAllCommand()
        {
            if (IsBusy) return;

            IsBusy = true;
            try
            {
                await Shell.Current.DisplayAlert("Synchronisation", "Synchronisation de tous les flux lancée. Veuillez patienter...", "OK");

                // *********** Logique de synchronisation réelle à implémenter ***********
                /* * Si la logique est complexe, elle devrait être dans le service (_rssService)
                 * await _rssService.SyncAllActivePlaylistsAsync(Playlists);
                */
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Erreur de Synchro", $"Échec de la synchronisation : {ex.Message}", "OK");
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}