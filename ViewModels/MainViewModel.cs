using Rss_feeder_prout.Models;
using Rss_feeder_prout.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Text;
using System.Globalization;
using Microsoft.Maui.Controls;
using System.Linq;
using System;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;

namespace Rss_feeder_prout.ViewModels
{
    public class MainViewModel : BaseViewModel
    {
        private readonly RssService _rssService;
        private readonly SQLiteService _dbService;

        // Collections pour les données
        public ObservableCollection<FeedPlaylist> AllPlaylists { get; } = new ObservableCollection<FeedPlaylist>();
        private ObservableCollection<RssItem> _allRssItems = new ObservableCollection<RssItem>();
        private ObservableCollection<RssItem> _filteredRssItems = new ObservableCollection<RssItem>();
        public ICommand MarkAllAsReadCommand => new Command(async () => await MarkAllAsReadAsync());

        private CancellationTokenSource _syncCts;

        public ObservableCollection<RssItem> FilteredRssItems
        {
            get => _filteredRssItems;
            set => SetProperty(ref _filteredRssItems, value);
        }
        private string _currentFilter = "All";  // "All", "Unread", "Read"
        private string _currentSort = "Newest"; // "Newest", "Oldest"
        private bool _isSearchVisible;

        public bool IsSearchVisible
        {
            get => _isSearchVisible;
            set => SetProperty(ref _isSearchVisible, value);
        }

        public ICommand ToggleSearchCommand => new Command(() => IsSearchVisible = !IsSearchVisible);

        public ICommand FilterCommand => new Command<string>((filterType) =>
        {
            _currentFilter = filterType;
            ApplyFilters();
        });

        public ICommand SortCommand => new Command<string>((sortType) =>
        {
            _currentSort = sortType;
            ApplyFilters();
        });

        public ObservableCollection<FeedSite> CurrentSites { get; } = new ObservableCollection<FeedSite>();

        // Classe pour gérer les groupes
        public class RssGroup : List<RssItem>
        {
            public string Name { get; private set; }
            public RssGroup(string name, List<RssItem> items) : base(items)
            {
                Name = name;
            }
        }

        private ObservableCollection<RssGroup> _groupedRssItems = new ObservableCollection<RssGroup>();
public ObservableCollection<RssGroup> GroupedRssItems
{
    get => _groupedRssItems;
    set => SetProperty(ref _groupedRssItems, value);
}

        private string _title = "Playlists";
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        // --- PROPRIÉTÉS D'ÉTAT DE VUE (3 Niveaux de Navigation) ---

        private readonly SemaphoreSlim _loadFeedSemaphore = new SemaphoreSlim(1, 1);

        private bool _showPlaylistsList = true;
        public bool ShowPlaylistsList
        {
            get => _showPlaylistsList;
            set
            {
                if (SetProperty(ref _showPlaylistsList, value))
                {
                    OnPropertyChanged(nameof(ShowSiteSelection));
                    OnPropertyChanged(nameof(ShowFeedView));
                }
            }
        }

        private bool _showSiteSelection = false;
        public bool ShowSiteSelection
        {
            get => _showSiteSelection;
            set
            {
                if (SetProperty(ref _showSiteSelection, value))
                {
                    OnPropertyChanged(nameof(ShowPlaylistsList));
                    OnPropertyChanged(nameof(ShowFeedView));
                }
            }
        }

        public bool ShowFeedView => !ShowPlaylistsList && !ShowSiteSelection;

        // --- PROPRIÉTÉS DE SÉLECTION ---

        public bool IsPlaylistSelected => CurrentPlaylist != null;

        private FeedPlaylist _currentPlaylist;
        public FeedPlaylist CurrentPlaylist
        {
            get => _currentPlaylist;
            set
            {
                if (SetProperty(ref _currentPlaylist, value))
                {
                    CurrentSite = null;
                    CurrentSites.Clear();
                    SearchText = string.Empty;

                    if (value != null)
                    {
                        Title = value.Name;

                        Task.Run(async () =>
                        {
                            var sites = await _dbService.GetSitesForPlaylistAsync(value.Id);
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                CurrentSites.Clear();
                                foreach (var site in sites)
                                {
                                    CurrentSites.Add(site);
                                }
                            });
                        });

                        ShowPlaylistsList = false;
                        ShowSiteSelection = true; // Niveau 2 : choix du site
                    }
                    else
                    {
                        Title = "Playlists";
                        ShowPlaylistsList = true; // Retour au niveau 1
                        ShowSiteSelection = false;
                    }
                    OnPropertyChanged(nameof(IsPlaylistSelected));
                    ((Command)DownloadAllContentsCommand).ChangeCanExecute();
                    // Mise à jour de l'état de la commande Sync All
                    ((Command)SyncAllSitesInPlaylistCommand).ChangeCanExecute();
                }
            }
        }

        private FeedSite _currentSite;
        public FeedSite CurrentSite
        {
            get => _currentSite;
            set
            {
                if (SetProperty(ref _currentSite, value))
                {
                    SearchText = string.Empty;
                    if (value != null)
                    {
                        Title = value.Name;
                        ShowSiteSelection = false;
                        Task.Run(ExecuteLoadFeedCommand);
                    }
                }
            }
        }

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    ExecuteSearchCommand();
                }
            }
        }

        private double _syncProgress;
        public double SyncProgress
        {
            get => _syncProgress;
            set => SetProperty(ref _syncProgress, value);
        }

        private string _syncStatus;
        public string SyncStatus
        {
            get => _syncStatus;
            set => SetProperty(ref _syncStatus, value);
        }

        // --- COMMANDES ---
        public ICommand LoadFeedCommand { get; }
        public ICommand OpenItemCommand { get; }
        public ICommand NavigateToPlaylistsCommand { get; }
        public ICommand SearchCommand { get; }
        public ICommand DownloadItemContentCommand { get; }
        public ICommand DownloadAllContentsCommand { get; }
        public ICommand GoBackToPlaylistsCommand { get; }
        public ICommand ShowAllItemsInPlaylistCommand { get; }
        public ICommand FullGlobalSyncCommand => new Command(async () => await ExecuteFullGlobalSyncCommand());

        // ✅ 1. DÉCLARATION DE LA NOUVELLE COMMANDE SYNC ALL
        public ICommand SyncAllSitesInPlaylistCommand { get; }

        private readonly SemaphoreSlim _syncSemaphore = new SemaphoreSlim(1, 1);


        public MainViewModel(RssService rssService, SQLiteService dbService)
        {
            _rssService = rssService;
            _dbService = dbService;

            // 🎯 LoadFeedCommand est désormais le "Sync All" du niveau 3
            LoadFeedCommand = new Command(async () => await ExecuteLoadFeedCommand());

            // 🎯 Commande avec logique de repli
            OpenItemCommand = new Command<RssItem>(async (item) => await ExecuteOpenItemCommand(item));

            NavigateToPlaylistsCommand = new Command(async () => await Shell.Current.GoToAsync("//PlaylistManagerPage"));
            SearchCommand = new Command(() => ExecuteSearchCommand());

            DownloadItemContentCommand = new Command<RssItem>(async (item) => await ExecuteDownloadItemContentCommand(item));
            DownloadAllContentsCommand = new Command(async () => await ExecuteDownloadAllContentsCommand(), () => CurrentPlaylist != null && !IsBusy);

            // LOGIQUE RETOUR MULTI-NIVEAUX
            GoBackToPlaylistsCommand = new Command(() =>
            {
                if (ShowFeedView) // De Articles (Niveau 3) -> Sélection de Site (Niveau 2)
                {
                    CurrentSite = null;
                    ShowSiteSelection = true;
                    _allRssItems.Clear();
                    FilteredRssItems.Clear();
                    Title = CurrentPlaylist?.Name ?? "Playlists";
                }
                else if (ShowSiteSelection) // De Sélection de Site (Niveau 2) -> Sélection de Playlist (Niveau 1)
                {
                    CurrentPlaylist = null;
                }
            });

            // Commande pour afficher TOUS les articles de la playlist (Niveau 2 -> Niveau 3)
            ShowAllItemsInPlaylistCommand = new Command(async () =>
            {
                CurrentSite = null;
                Title = CurrentPlaylist.Name;
                ShowSiteSelection = false;
                await ExecuteLoadFeedCommand();
            }, () => CurrentPlaylist != null && !IsBusy);

            // ✅ 2. INITIALISATION DE LA NOUVELLE COMMANDE SYNC ALL
            // Elle est exécutable si une playlist est sélectionnée et si le ViewModel n'est pas déjà occupé.
            SyncAllSitesInPlaylistCommand = new Command(async () => await ExecuteSyncAllSitesInPlaylistCommand(), () => CurrentPlaylist != null && !IsBusy);


            // Chargement initial au démarrage
            Task.Run(async () =>
            {
                await ExecuteInitialLoad();
            });
        }

        // --- Logique de Chargement Initial (Inchangée) ---

        private async Task MarkAllAsReadAsync()
        {
            if (FilteredRssItems == null || !FilteredRssItems.Any())
                return;

            // 1. Demander confirmation
            bool confirm = await Application.Current.MainPage.DisplayAlert(
                "Lecture",
                $"Marquer les {FilteredRssItems.Count} articles comme lus ?",
                "Oui", "Non");

            if (!confirm) return;

            try
            {
                // 2. Modifier en base de données et dans la liste
                foreach (var item in FilteredRssItems.ToList()) // .ToList() pour éviter les erreurs de modification de collection
                {
                    if (!item.IsRead)
                    {
                        item.IsRead = true;
                        // Sauvegarde SQL via ton service existant
                        await _dbService.UpdateRssItemAsync(item);
                    }
                }

                // 3. Appliquer les filtres pour rafraîchir l'écran
                // Si l'utilisateur est sur le filtre "Non lus", les articles disparaîtront automatiquement
                ApplyFilters();

                Debug.WriteLine("✅ Tous les articles affichés ont été marqués comme lus.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erreur lors du marquage : {ex.Message}");
            }
        }

        private void ApplyFilters()
        {
            // On part de la liste complète chargée depuis la DB ou le RSS
            var query = _allRssItems.AsEnumerable();

            // 1. Filtrage par texte (Recherche)
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string search = RemoveAccents(SearchText.ToLowerInvariant());
                query = query.Where(item =>
                    RemoveAccents(item.Title?.ToLowerInvariant() ?? "").Contains(search) ||
                    RemoveAccents(item.Summary?.ToLowerInvariant() ?? "").Contains(search));
            }

            // 2. Filtrage par statut (Filtre Chips)
            query = _currentFilter switch
            {
                "Unread" => query.Where(x => !x.IsRead),
                "Read" => query.Where(x => x.IsRead),
                _ => query // "All"
            };

            // 3. Tri (Nouveau vs Ancien)
            query = _currentSort switch
            {
                "Oldest" => query.OrderBy(x => x.PublishDate),
                "Newest" or _ => query.OrderByDescending(x => x.PublishDate)
            };

            // Mise à jour de la collection affichée à l'écran
            FilteredRssItems = new ObservableCollection<RssItem>(query.ToList());
        }

        public void StopSync()
        {
            _syncCts?.Cancel();
            SyncStatus = "Arrêt en cours...";
        }

        public async Task ExecuteFullGlobalSyncCommand()
        {
            if (!_syncSemaphore.Wait(0)) return;

            _syncCts = new CancellationTokenSource();
            var token = _syncCts.Token;

            try
            {
                IsBusy = true;
                SyncProgress = 0;
                SyncStatus = "Initialisation...";

                if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                {
                    await Shell.Current.DisplayAlert("Pas de connexion", "Internet est requis.", "OK");
                    return;
                }

                var allPlaylists = await _dbService.GetPlaylistsAsync();
                var playlistsList = allPlaylists.ToList();

                if (!playlistsList.Any()) return;

                double totalSteps = playlistsList.Count;
                double currentStep = 0;

                foreach (var playlist in playlistsList)
                {
                    // Vérification si l'utilisateur a cliqué sur STOP
                    if (token.IsCancellationRequested) break;

                    currentStep++;
                    SyncStatus = $"Sync : {playlist.Name} ({currentStep}/{totalSteps})";

                    // Mise à jour de la progression
                    SyncProgress = currentStep / totalSteps;

                    try
                    {
                        // Note: Tu peux passer 'token' à tes méthodes de service si tu veux un arrêt instantané
                        await _rssService.SynchronizeSitesInPlaylistAsync(playlist.Id);
                    }
                    catch (Exception ex) { Debug.WriteLine($"[SYNC ERR] {ex.Message}"); }

                    // Gestion des icônes
                    var sites = await _dbService.GetSitesByPlaylistIdAsync(playlist.Id);
                    foreach (var site in sites)
                    {
                        if (token.IsCancellationRequested) break;

                        if (string.IsNullOrEmpty(site.IconUrl) && !string.IsNullOrEmpty(site.FeedUrl))
                        {
                            try
                            {
                                var uri = new Uri(site.FeedUrl);
                                site.IconUrl = $"https://www.google.com/s2/favicons?sz=128&domain={uri.Host}";
                                await _dbService.UpdateSiteAsync(site);
                            }
                            catch { }
                        }

                        if (!string.IsNullOrEmpty(site.IconUrl) && string.IsNullOrEmpty(site.LocalIconPath))
                        {
                            string localPath = await _rssService.DownloadAndSaveIconAsync(site.IconUrl, site.Name);
                            if (!string.IsNullOrEmpty(localPath))
                            {
                                site.LocalIconPath = localPath;
                                await _dbService.UpdateSiteAsync(site);
                            }
                        }
                    }
                }

                if (token.IsCancellationRequested)
                {
                    SyncStatus = "Synchronisation annulée par l'utilisateur.";
                    await Shell.Current.DisplayAlert("Annulé", "Le téléchargement a été stoppé.", "OK");
                }
                else
                {
                    SyncStatus = "Mise à jour terminée !";
                    await Shell.Current.DisplayAlert("Succès", "L'intégralité de vos flux est à jour.", "OK");
                }

                if (ShowPlaylistsList) await ExecuteInitialLoad();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GLOBAL SYNC ERROR]: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                SyncProgress = 0;
                SyncStatus = string.Empty;
                _syncSemaphore.Release();
                _syncCts?.Dispose();
                _syncCts = null;
            }
        }

        private async Task ExecuteInitialLoad()
        {
            // 1. Éviter les chargements multiples simultanés
            if (IsBusy) return;

            try
            {
                IsBusy = true;

                // 2. Récupération des données hors du thread UI (plus rapide)
                var playlists = await _dbService.GetPlaylistsAsync();

                // 3. Tri alphabétique pour que l'utilisateur s'y retrouve
                var playlistsList = playlists?
                    .OrderBy(p => p.Name)
                    .ToList() ?? new List<FeedPlaylist>();

                // 4. Bascule sur le thread principal pour toucher à l'UI
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    // 5. Mise à jour de la collection sans tout reconstruire si identique
                    // (Optionnel : aide à la fluidité visuelle)
                    AllPlaylists.Clear();
                    foreach (var p in playlistsList)
                    {
                        AllPlaylists.Add(p);
                    }

                    // 6. Mise à jour de l'état de vue
                    // Ton 'set' de ShowPlaylistsList déclenche OnPropertyChanged pour les autres vues.
                    ShowPlaylistsList = AllPlaylists.Any();

                    // 7. Titre dynamique et propre
                    Title = ShowPlaylistsList ? "Mes Playlists 💾" : "Aucune Playlist trouvée";
                });
            }
            catch (Exception ex)
            {
                // 8. Log pour le débogage technique
                Debug.WriteLine($"[INIT ERROR] {ex.Message}");

                // 9. Feedback à l'utilisateur en cas de crash DB
                await Shell.Current.DisplayAlert("Erreur", "Impossible de charger les données locales.", "OK");
            }
            finally
            {
                // 10. Toujours libérer l'indicateur de chargement
                IsBusy = false;
            }
        }


        private async Task ExecuteSyncAllSitesInPlaylistCommand()
        {
            // 1. Protection contre le double-clic
            if (!_syncSemaphore.Wait(0)) return;

            if (CurrentPlaylist == null || IsBusy)
            {
                _syncSemaphore.Release();
                return;
            }

            try
            {
                IsBusy = true;
                ((Command)SyncAllSitesInPlaylistCommand).ChangeCanExecute();

                // 2. Vérification Internet
                if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                {
                    await Shell.Current.DisplayAlert("Pas de connexion", "Une connexion internet est requise pour synchroniser.", "OK");
                    return;
                }

                Debug.WriteLine($"[SYNC] Début de la synchronisation pour: {CurrentPlaylist.Name}");

                // 3. Synchronisation des articles RSS
                var syncTask = _rssService.SynchronizeSitesInPlaylistAsync(CurrentPlaylist.Id);
                if (await Task.WhenAny(syncTask, Task.Delay(TimeSpan.FromMinutes(2))) != syncTask)
                {
                    throw new TimeoutException("La synchronisation des articles a pris trop de temps.");
                }

                // 4. Gestion des Icônes (Auto-récupération + Téléchargement Offline)
                var sites = await _dbService.GetSitesByPlaylistIdAsync(CurrentPlaylist.Id);

                foreach (var site in sites)
                {
                    bool needsUpdate = false;

                    // A. Si aucune URL d'icône n'existe, on en génère une automatiquement
                    if (string.IsNullOrEmpty(site.IconUrl) && !string.IsNullOrEmpty(site.FeedUrl))
                    {
                        try
                        {
                            var uri = new Uri(site.FeedUrl);
                            // Utilisation de l'API Google pour trouver le favicon en haute résolution (128px)
                            site.IconUrl = $"https://www.google.com/s2/favicons?sz=128&domain={uri.Host}";
                            needsUpdate = true;
                        }
                        catch { /* URL de flux malformée, on ignore */ }
                    }

                    // B. Si on a une URL d'icône mais pas encore de fichier local, on télécharge
                    if (!string.IsNullOrEmpty(site.IconUrl) && string.IsNullOrEmpty(site.LocalIconPath))
                    {
                        try
                        {
                            string localPath = await _rssService.DownloadAndSaveIconAsync(site.IconUrl, site.Name);

                            if (!string.IsNullOrEmpty(localPath))
                            {
                                site.LocalIconPath = localPath;
                                needsUpdate = true;
                            }
                        }
                        catch (Exception iconEx)
                        {
                            Debug.WriteLine($"[ICON ERROR] {site.Name}: {iconEx.Message}");
                        }
                    }

                    // C. Sauvegarde en base et mise à jour de l'UI si nécessaire
                    if (needsUpdate)
                    {
                        await _dbService.UpdateSiteAsync(site);

                        // Met à jour l'objet directement dans la liste affichée à l'écran
                        var siteInUI = CurrentSites.FirstOrDefault(s => s.Id == site.Id);
                        if (siteInUI != null)
                        {
                            siteInUI.IconUrl = site.IconUrl;
                            siteInUI.LocalIconPath = site.LocalIconPath;
                            // Note: Le changement d'icône sera instantané grâce au OnPropertyChanged dans le modèle
                        }
                    }
                }

                // 5. Rafraîchissement final de la vue des articles
                if (ShowFeedView)
                {
                    await ExecuteLoadFeedCommand();
                }

                await Shell.Current.DisplayAlert("Succès", $"Mise à jour terminée pour '{CurrentPlaylist.Name}'.", "OK");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SYNC ERROR]: {ex.Message}");
                string errorMsg = ex is TimeoutException ? "Délai d'attente dépassé." : "Une erreur est survenue lors de la mise à jour.";
                await Shell.Current.DisplayAlert("Erreur de Synchro", errorMsg, "OK");
            }
            finally
            {
                IsBusy = false;
                _syncSemaphore.Release();

                // Mise à jour de l'état des boutons sur le thread principal
                MainThread.BeginInvokeOnMainThread(() => {
                    ((Command)SyncAllSitesInPlaylistCommand).ChangeCanExecute();
                    ((Command)DownloadAllContentsCommand).ChangeCanExecute();
                });
            }
        }

        

        // --- Logique de Chargement et de Rafraîchissement (Cœur) (Inchangée) ---
        private async Task ExecuteLoadFeedCommand()
        {
            if (IsBusy || CurrentPlaylist == null) return;

            try
            {
                IsBusy = true;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Title = CurrentSite?.Name ?? CurrentPlaylist.Name;
                });

                System.Collections.Generic.IEnumerable<RssItem> items;

                // Si CurrentSite est null, on charge tous les articles de la playlist (et on lance la mise à jour des flux en arrière-plan)
                if (CurrentSite == null && CurrentSites.Any())
                {
                    // 🎯 Appelle la mise à jour pour TOUS les sites de la playlist.
                    items = await _rssService.UpdateAndGetFeedItemsForSitesAsync(CurrentPlaylist);
                }
                // Si CurrentSite est sélectionné, on charge les articles de ce site unique.
                else if (CurrentSite != null)
                {
                    // 🎯 Appelle la mise à jour pour le site unique.
                    items = await _rssService.UpdateAndGetFeedItemsForSitesAsync(CurrentPlaylist, CurrentSite);
                }
                else
                {
                    items = Enumerable.Empty<RssItem>();
                }


                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var currentItems = _allRssItems.ToList();
                    _allRssItems.Clear();

                    foreach (var item in items.OrderByDescending(i => i.PublishDate))
                    {
                        var existingItem = currentItems.FirstOrDefault(i => i.ArticleGuid == item.ArticleGuid);

                        if (existingItem != null)
                        {
                            // Transfert des états locaux pour éviter de perdre les marques de lecture/téléchargement si 
                            // l'article est en cache mais la MAJ n'a pas encore été lue par la DB.
                            existingItem.IsRead = item.IsRead;
                            existingItem.IsDownloaded = item.IsDownloaded;
                            _allRssItems.Add(existingItem);
                        }
                        else
                        {
                            _allRssItems.Add(item);
                        }
                    }

                    ExecuteSearchCommand();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading feed: {ex.Message}");
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await Shell.Current.DisplayAlert("Erreur de Chargement", "Impossible de charger les flux. Vérifiez la connexion ou les URLs.", "OK");
                });
            }
            finally
            {
                IsBusy = false;
                ((Command)DownloadAllContentsCommand).ChangeCanExecute();
                ((Command)ShowAllItemsInPlaylistCommand).ChangeCanExecute();
                ((Command)SyncAllSitesInPlaylistCommand).ChangeCanExecute();
            }
        }

        // --- 1. Téléchargement d'un Seul Article (avec Retry et Timeout) ---
        private async Task ExecuteDownloadItemContentCommand(RssItem item)
        {
            if (item == null || item.IsDownloaded || item.IsBusy) return;

            // Vérification réseau
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                await Shell.Current.DisplayAlert("Mode Hors Ligne", "Connectez-vous pour télécharger cet article.", "OK");
                return;
            }

            try
            {
                item.IsBusy = true;

                // Timeout de 45 secondes pour éviter de bloquer sur un lien mort
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

                // Logique de Retry (3 tentatives max)
                int retryCount = 0;
                bool success = false;

                while (retryCount < 3 && !success)
                {
                    try
                    {
                        await _rssService.DownloadAndSaveItemContentAsync(item);
                        success = true;
                    }
                    catch { retryCount++; if (retryCount == 3) throw; }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DOWNLOAD ERROR] {ex.Message}");
                await Shell.Current.DisplayAlert("Erreur", "Le serveur ne répond pas ou le lien est invalide.", "OK");
            }
            finally
            {
                item.IsBusy = false;
            }
        }

        // --- 2. Synchronisation de la Playlist (Parallélisme & Progression) ---
        private async Task ExecuteDownloadAllContentsCommand()
        {
            if (CurrentPlaylist == null || IsBusy) return;

            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                await Shell.Current.DisplayAlert("Action Impossible", "La synchronisation nécessite une connexion Wi-Fi ou mobile.", "OK");
                return;
            }

            try
            {
                IsBusy = true;

                // On définit un timeout global de 5 minutes pour la playlist
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

                // Amélioration : On passe par une méthode qui gère le parallélisme (SemaphoreSlim)
                // pour ne pas saturer le processeur ou la connexion.
                await _rssService.DownloadAllContentForPlaylistAsync(CurrentPlaylist.Id);

                // Mise à jour de l'UI
                await ExecuteLoadFeedCommand();

                await Shell.Current.DisplayAlert("Synchro Terminée", "Articles disponibles hors ligne.", "OK");
            }
            catch (OperationCanceledException)
            {
                await Shell.Current.DisplayAlert("Délai dépassé", "La synchro a pris trop de temps (timeout).", "OK");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SYNC ERROR] {ex.Message}");
                await Shell.Current.DisplayAlert("Erreur de synchro", "Certains articles n'ont pu être récupérés.", "OK");
            }
            finally
            {
                IsBusy = false;
            }
        }

        // --- 3. Ouverture d'Article (Sauvegarde Prioritaire & Sécurité) ---
        private async Task ExecuteOpenItemCommand(RssItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Link))
            {
                await Shell.Current.DisplayAlert("Erreur", "Lien d'article introuvable.", "OK");
                return;
            }

            // Amélioration : Marquer comme lu immédiatement en tâche de fond (Fire and Forget)
            // On n'attend pas la navigation pour sauver l'état "lu".
            if (!item.IsRead)
            {
                item.IsRead = true;
                _ = Task.Run(async () => await _dbService.MarkItemAsReadAsync(item));
            }

            // 1. Navigation Interne
            if (item.Id > 0 && item.IsDownloaded)
            {
                try
                {
                    // Vérification simple du format avant d'ouvrir
                    if (item.Link.EndsWith(".pdf") || item.Link.EndsWith(".zip"))
                    {
                        await OpenExternalLink(item);
                        return;
                    }

                    await Shell.Current.GoToAsync($"ArticleDetailPage?itemId={item.Id}", animate: true);
                    return;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NAV ERROR] {ex.Message}. Bascule vers lien externe.");
                }
            }

            // 2. Repli vers navigateur externe (si non téléchargé ou erreur nav)
            await OpenExternalLink(item);
        }

        // Méthode utilitaire pour ouvrir le lien dans le navigateur par défaut (Inchangée)
        private async Task OpenExternalLink(RssItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Link)) return;

            try
            {
                string cleanUrl = item.Link.Trim();

                if (!Uri.TryCreate(cleanUrl, UriKind.Absolute, out Uri uriResult))
                {
                    await Shell.Current.DisplayAlert("Lien Invalide", "L'adresse Web est mal formée.", "OK");
                    return;
                }

                // --- OUVERTURE DANS LE NAVIGATEUR INTERNE ---
                // SystemPreferred ouvre Chrome Custom Tabs (Android) ou Safari View Controller (iOS)
                // Cela permet de garder l'utilisateur "dans" ton app avec un bouton "Fermer" en haut.
                await Browser.Default.OpenAsync(uriResult, new BrowserLaunchOptions
                {
                    LaunchMode = BrowserLaunchMode.SystemPreferred,
                    TitleMode = BrowserTitleMode.Show,
                    PreferredToolbarColor = Colors.Black, // Tu peux mettre la couleur de ton thème ici
                    PreferredControlColor = Colors.White
                });

                // --- MARQUAGE COMME LU ---
                if (!item.IsRead)
                {
                    item.IsRead = true;
                    // Mise à jour en arrière-plan pour ne pas ralentir l'ouverture
                    _ = Task.Run(async () => await _dbService.MarkItemAsReadAsync(item));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BROWSER ERROR] {ex.Message}");
                // Si le mode interne échoue, on peut tenter une dernière fois en externe
                try
                {
                    await Launcher.Default.OpenAsync(item.Link);
                }
                catch
                {
                    await Shell.Current.DisplayAlert("Erreur", "Impossible d'ouvrir le lien.", "OK");
                }
            }
        }

        // --- Logique de Recherche Avancée (Optimisée) ---
        private void ExecuteSearchCommand()
        {
            // 1. Si la barre est vide, on recharge tout proprement
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                FilteredRssItems = new ObservableCollection<RssItem>(_allRssItems);
                return;
            }

            // 2. Normalisation de la requête (minuscules et retrait des accents)
            string query = RemoveAccents(SearchText.Trim().ToLowerInvariant());
            string[] keywords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // 3. Filtrage haute performance
            var filteredList = _allRssItems.Where(item =>
            {
                // Normalisation du contenu de l'article pour la comparaison
                string title = RemoveAccents(item.Title?.ToLowerInvariant() ?? "");
                string summary = RemoveAccents(item.Summary?.ToLowerInvariant() ?? "");
                string site = RemoveAccents(item.SiteName?.ToLowerInvariant() ?? "");

                // L'article doit contenir TOUS les mots-clés de la recherche
                return keywords.All(key =>
                    title.Contains(key) ||
                    summary.Contains(key) ||
                    site.Contains(key));
            }).ToList();

            // 4. Mise à jour de la collection liée à la vue
            FilteredRssItems = new ObservableCollection<RssItem>(filteredList);
        }

        // Méthode helper pour gérer les accents (É -> E)
        private string RemoveAccents(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            return new string(
                text.Normalize(NormalizationForm.FormD)
                    .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    .ToArray()
            ).Normalize(NormalizationForm.FormC);
        }
    }
}