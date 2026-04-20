using Rss_feeder_prout.Models;
using Rss_feeder_prout.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using System.Diagnostics;
using System.Linq;
using System;

namespace Rss_feeder_prout.ViewModels
{
    [QueryProperty(nameof(PlaylistId), "id")]
    public class PlaylistDetailViewModel : BaseViewModel
    {
        private readonly SQLiteService _dbService;

        // --- Propriétés de la Playlist ---

        private FeedPlaylist _currentPlaylist;
        public FeedPlaylist CurrentPlaylist
        {
            get => _currentPlaylist;
            set => SetProperty(ref _currentPlaylist, value);
        }

        // 🎯 NOUVEAU : Collection pour afficher et gérer les sites
        public ObservableCollection<FeedSite> Sites { get; } = new ObservableCollection<FeedSite>();

        // --- Propriété pour l'entrée de texte du nouveau site ---

        private string _newUrlText;
        public string NewUrlText
        {
            get => _newUrlText;
            set
            {
                if (SetProperty(ref _newUrlText, value))
                {
                    ((Command)AddSiteCommand).ChangeCanExecute(); // Réévalue CanExecuteAddSite
                }
            }
        }

        // --- Propriété de Navigation (ID reçu par l'URL) ---

        private int _playlistId;
        public int PlaylistId
        {
            get => _playlistId;
            set
            {
                if (SetProperty(ref _playlistId, value))
                {
                    // Charge la playlist et les sites dès que l'ID est reçu
                    Task.Run(LoadPlaylistAndSites);
                }
            }
        }

        // --- Commandes ---
        public ICommand SaveCommand { get; }
        // 🎯 MODIFIÉ : Commande d'ajout de site (plus d'URL brute)
        public ICommand AddSiteCommand { get; }
        // 🎯 MODIFIÉ : Commande de suppression de site
        public ICommand DeleteSiteCommand { get; }

        public PlaylistDetailViewModel(SQLiteService dbService)
        {
            _dbService = dbService;

            // Initialisation avec une playlist vide pour éviter les NullReferenceException
            // Suppression de la référence à UrlsSerialized ici.
            CurrentPlaylist = new FeedPlaylist();

            SaveCommand = new Command(async () => await ExecuteSaveCommand());
            // 🎯 MODIFIÉ : Renommage de la commande
            AddSiteCommand = new Command(async () => await ExecuteAddSiteCommand(), CanExecuteAddSite);
            // 🎯 MODIFIÉ : Utilise FeedSite comme paramètre
            DeleteSiteCommand = new Command<FeedSite>(async (s) => await ExecuteDeleteSiteCommand(s));
        }

        // --- Méthodes de chargement ---

        private async Task LoadPlaylistAndSites()
        {
            IsBusy = true;
            try
            {
                // 1. Charger la Playlist
                var playlist = await _dbService.GetPlaylistAsync(PlaylistId);
                if (playlist != null)
                {
                    CurrentPlaylist = playlist;
                }

                // 2. Charger les Sites liés
                Sites.Clear();
                var sitesList = await _dbService.GetSitesForPlaylistAsync(PlaylistId);
                foreach (var site in sitesList)
                {
                    Sites.Add(site);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] Failed to load playlist ID {PlaylistId} or sites: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        // --- Commandes de Site (Remplacement des commandes d'URL) ---

        private async Task ExecuteAddSiteCommand()
        {
            if (!CanExecuteAddSite()) return;

            string url = NewUrlText.Trim();

            // 🎯 Vérification : Est-ce une URL valide? (Simplifié)
            if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                await Shell.Current.DisplayAlert("Erreur", "Veuillez entrer une URL valide.", "OK");
                return;
            }

            // Vérification : Le site existe-t-il déjà pour cette playlist ?
            if (Sites.Any(s => s.FeedUrl.Equals(url, StringComparison.OrdinalIgnoreCase)))
            {
                await Shell.Current.DisplayAlert("Attention", "Ce flux RSS est déjà dans la playlist.", "OK");
                return;
            }

            // 1. Créer le nouvel objet FeedSite
            // On peut dériver un nom simple ou le laisser à l'utilisateur si on veut un champ Name
            // Ici, on utilise l'hôte de l'URL comme nom par défaut.
            string defaultName = new Uri(url).Host.Replace("www.", "");
            var newSite = new FeedSite
            {
                Name = defaultName,
                FeedUrl = url,
                PlaylistId = CurrentPlaylist.Id
            };

            // 2. Sauvegarder dans la DB
            await _dbService.SaveSiteAsync(newSite);

            // 3. Ajouter à la collection Observable pour la mise à jour UI
            Sites.Add(newSite);

            // 4. Réinitialiser le champ de saisie
            NewUrlText = string.Empty;
        }

        private bool CanExecuteAddSite()
        {
            return !string.IsNullOrWhiteSpace(NewUrlText);
        }

        private async Task ExecuteDeleteSiteCommand(FeedSite site)
        {
            if (site == null) return;

            // 1. Supprimer de la DB (cela supprime aussi les articles liés dans SQLiteService)
            await _dbService.DeleteSiteAsync(site);

            // 2. Supprimer de la collection Observable
            Sites.Remove(site);
        }

        // --- Commande de Sauvegarde de la Playlist (Mise à jour du nom) ---

        private async Task ExecuteSaveCommand()
        {
            if (IsBusy || CurrentPlaylist == null) return;
            IsBusy = true;

            try
            {
                // Valider le nom
                if (string.IsNullOrWhiteSpace(CurrentPlaylist.Name))
                {
                    await Shell.Current.DisplayAlert("Erreur", "Le nom de la playlist ne peut être vide.", "OK");
                    return;
                }

                // Ne fait que mettre à jour l'objet Playlist (surtout le nom)
                await _dbService.SavePlaylistAsync(CurrentPlaylist);

                await Shell.Current.DisplayAlert("Succès", $"Playlist '{CurrentPlaylist.Name}' sauvegardée.", "OK");

                // Retour à la page précédente
                await Shell.Current.GoToAsync("..");
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Erreur de sauvegarde", $"Échec : {ex.Message}", "OK");
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}