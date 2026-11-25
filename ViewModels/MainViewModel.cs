using Rss_feeder_prout.Helpers;
using Rss_feeder_prout.Models;
using Rss_feeder_prout.Services;
using Rss_feeder_prout.Views;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Networking;

namespace Rss_feeder_prout.ViewModels
{
    /// <summary>
    /// Accueil : flux intelligents, catégories avec compteurs, synchronisation globale (non bloquante).
    /// </summary>
    public class MainViewModel : BaseViewModel
    {
        private readonly RssService _rssService;
        private readonly SQLiteService _dbService;
        private readonly SyncScheduler _scheduler;

        public SyncService Sync { get; }

        public ObservableCollection<FeedPlaylist> AllPlaylists { get; } = new ObservableCollection<FeedPlaylist>();

        // ------------------------------------------------------------------
        // Compteurs
        // ------------------------------------------------------------------

        private SmartCounts _counts = new();
        public SmartCounts Counts
        {
            get => _counts;
            set
            {
                if (SetProperty(ref _counts, value))
                    Notify(nameof(CountAllText), nameof(CountUnreadText), nameof(CountTodayText),
                           nameof(CountFavoritesText), nameof(CountReadLaterText), nameof(CountDownloadedText), nameof(HeroText));
            }
        }

        public string CountAllText => Counts.All.ToString("N0");
        public string CountUnreadText => Counts.Unread.ToString("N0");
        public string CountTodayText => Counts.Today.ToString("N0");
        public string CountFavoritesText => Counts.Favorites.ToString("N0");
        public string CountReadLaterText => Counts.ReadLater.ToString("N0");
        public string CountDownloadedText => Counts.Downloaded.ToString("N0");

        public string HeroText => Counts.Unread switch
        {
            0 => "Vous êtes à jour.",
            1 => "1 article vous attend.",
            _ => $"{Counts.Unread:N0} articles vous attendent."
        };

        public string Greeting => DateHelper.Greeting();
        public string TodayText => DateHelper.TodayText();

        private string _lastSyncText = string.Empty;
        public string LastSyncText
        {
            get => _lastSyncText;
            set => SetProperty(ref _lastSyncText, value);
        }

        private string _nextSyncText = string.Empty;
        public string NextSyncText
        {
            get => _nextSyncText;
            set => SetProperty(ref _nextSyncText, value);
        }

        private bool _isOffline;
        public bool IsOffline
        {
            get => _isOffline;
            set => SetProperty(ref _isOffline, value);
        }

        private bool _isRefreshing;
        public bool IsRefreshing
        {
            get => _isRefreshing;
            set => SetProperty(ref _isRefreshing, value);
        }

        private bool _hasPlaylists = true;
        public bool HasPlaylists
        {
            get => _hasPlaylists;
            set => SetProperty(ref _hasPlaylists, value);
        }

        // ------------------------------------------------------------------
        // Commandes
        // ------------------------------------------------------------------

        public ICommand FullGlobalSyncCommand { get; }
        public ICommand QuickSyncCommand { get; }
        public ICommand StopSyncCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand OpenPlaylistCommand { get; }
        public ICommand OpenSmartCommand { get; }
        public ICommand NavigateToPlaylistsCommand { get; }
        public ICommand MarkAllAsReadCommand { get; }
        public ICommand PlaylistMenuCommand { get; }
        public ICommand OpenSearchCommand { get; }

        public MainViewModel(RssService rssService, SQLiteService dbService, SyncService sync, SyncScheduler scheduler)
        {
            _rssService = rssService;
            _dbService = dbService;
            _scheduler = scheduler;
            Sync = sync;

            FullGlobalSyncCommand = new Command(async () => await ExecuteFullGlobalSyncCommand());
            QuickSyncCommand = new Command(async () => await StartSyncAsync(new SyncRequest
            {
                DownloadContent = false,
                ImageMode = ImageMode.None,
                Trigger = SyncTrigger.Pull,
                Label = "Synchro rapide"
            }));
            StopSyncCommand = new Command(StopSync);
            RefreshCommand = new Command(async () =>
            {
                IsRefreshing = true;
                await LoadAsync();
                IsRefreshing = false;
                if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet && !Sync.IsRunning)
                    QuickSyncCommand.Execute(null);
            });
            OpenPlaylistCommand = new Command<FeedPlaylist>(async p =>
            {
                if (p != null) await UiHelper.GoToAsync($"{nameof(SourcesPage)}?playlistId={p.Id}");
            });
            OpenSmartCommand = new Command<string>(async s => await OpenSmartAsync(s));
            NavigateToPlaylistsCommand = new Command(async () => await UiHelper.GoToAsync(nameof(PlaylistManagerPage)));
            MarkAllAsReadCommand = new Command(async () => await MarkAllAsReadAsync());
            PlaylistMenuCommand = new Command<FeedPlaylist>(async p => await PlaylistMenuAsync(p));
            OpenSearchCommand = new Command(async () => await Shell.Current.GoToAsync("//searchtab"));

            Sync.SyncCompleted += (s, e) => _ = LoadAsync();
            _scheduler.ScheduleChanged += (s, e) => UpdateSyncTexts();
            _dbService.DatabaseReset += (s, e) => _ = LoadAsync();
            Connectivity.Current.ConnectivityChanged += (s, e) =>
                MainThread.BeginInvokeOnMainThread(() => IsOffline = e.NetworkAccess != NetworkAccess.Internet);
        }

        // ------------------------------------------------------------------
        // Chargement
        // ------------------------------------------------------------------

        public async Task LoadAsync()
        {
            try
            {
                var playlists = await _dbService.GetPlaylistsAsync();
                var unread = await _dbService.GetUnreadCountsByPlaylistAsync();
                var siteCounts = await _dbService.GetSiteCountsByPlaylistAsync();
                var counts = await _dbService.GetSmartCountsAsync();

                foreach (var p in playlists)
                {
                    p.UnreadCount = unread.TryGetValue(p.Id, out var u) ? u : 0;
                    p.SiteCount = siteCounts.TryGetValue(p.Id, out var c) ? c : 0;
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    AllPlaylists.Clear();
                    foreach (var p in playlists) AllPlaylists.Add(p);
                    HasPlaylists = AllPlaylists.Count > 0;
                    Counts = counts;
                    IsOffline = Connectivity.Current.NetworkAccess != NetworkAccess.Internet;
                    Notify(nameof(Greeting), nameof(TodayText));
                    UpdateSyncTexts();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[INIT ERROR] {ex.Message}");
            }
        }

        private void UpdateSyncTexts()
        {
            var last = AppSettings.LastSyncUtc;
            LastSyncText = last == DateTime.MinValue
                ? "Jamais synchronisé"
                : "Dernière synchro " + DateHelper.ToRelative(DateTime.SpecifyKind(last, DateTimeKind.Utc).ToLocalTime());
            NextSyncText = _scheduler.NextRunText;
        }

        // ------------------------------------------------------------------
        // Synchronisation
        // ------------------------------------------------------------------

        /// <summary>Conservé : « Tout synchroniser » — propose maintenant le choix (contenu, images).</summary>
        public async Task ExecuteFullGlobalSyncCommand()
        {
            if (Sync.IsRunning)
            {
                UiHelper.Toast("Une synchronisation est déjà en cours");
                return;
            }

            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                await UiHelper.AlertAsync("Pas de connexion", "Internet est requis.");
                return;
            }

            var request = await SyncOptionsPage.AskAsync();
            if (request == null) return;
            request.Label = "Synchronisation globale";
            await StartSyncAsync(request);
        }

        private async Task StartSyncAsync(SyncRequest request)
        {
            if (Sync.IsRunning) return;
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                UiHelper.Toast("Hors connexion");
                return;
            }

            // Non bloquant : la synchro tourne en arrière-plan, l'utilisateur continue à lire
            _ = Task.Run(async () =>
            {
                var result = await Sync.RunAsync(request);
                if (!result.Skipped)
                {
                    UiHelper.Toast(result.Cancelled ? "Synchronisation annulée" : "Synchro terminée · " + result.Summary);
                    _scheduler.Reschedule();
                }
                else
                {
                    UiHelper.Toast(result.Message);
                }
            });
            await Task.CompletedTask;
        }

        /// <summary>Conservé : arrêt de la synchro.</summary>
        public void StopSync()
        {
            Sync.Cancel();
        }

        // ------------------------------------------------------------------
        // Navigation
        // ------------------------------------------------------------------

        private async Task OpenSmartAsync(string? smart)
        {
            if (string.IsNullOrEmpty(smart)) return;
            var title = smart switch
            {
                "Unread" => "Non lus",
                "Today" => "Aujourd'hui",
                "Favorites" => "Favoris",
                "ReadLater" => "À lire plus tard",
                "Downloaded" => "Hors-ligne",
                _ => "Tous les articles"
            };
            await UiHelper.GoToAsync($"{nameof(ArticleListPage)}?smart={smart}&title={Uri.EscapeDataString(title)}");
        }

        /// <summary>Conservé : tout marquer comme lu (toutes catégories).</summary>
        private async Task MarkAllAsReadAsync()
        {
            if (Counts.Unread == 0) { UiHelper.Toast("Tout est déjà lu"); return; }
            if (!await UiHelper.ConfirmAsync("Lecture", $"Marquer les {Counts.Unread} articles non lus comme lus ?", "Oui", "Non")) return;
            await _dbService.MarkAllReadAsync(new ArticleQuery());
            await LoadAsync();
        }

        private async Task PlaylistMenuAsync(FeedPlaylist? p)
        {
            if (p == null) return;
            var choice = await UiHelper.ActionSheetAsync(p.Name, "Annuler", null,
                "Voir les articles", "Voir les non lus", "Synchroniser cette catégorie", "Tout marquer comme lu", "Gérer les sources");
            switch (choice)
            {
                case "Voir les articles":
                    await UiHelper.GoToAsync($"{nameof(ArticleListPage)}?playlistId={p.Id}&title={Uri.EscapeDataString(p.Name)}");
                    break;
                case "Voir les non lus":
                    await UiHelper.GoToAsync($"{nameof(ArticleListPage)}?playlistId={p.Id}&smart=Unread&title={Uri.EscapeDataString(p.Name)}");
                    break;
                case "Synchroniser cette catégorie":
                    var request = await SyncOptionsPage.AskAsync();
                    if (request == null) return;
                    request.PlaylistIds = new[] { p.Id };
                    request.OnlyActivePlaylists = false;
                    request.Label = $"Synchro · {p.Name}";
                    await StartSyncAsync(request);
                    break;
                case "Tout marquer comme lu":
                    await _dbService.MarkAllReadAsync(new ArticleQuery { PlaylistId = p.Id });
                    await LoadAsync();
                    break;
                case "Gérer les sources":
                    await UiHelper.GoToAsync($"{nameof(PlaylistDetailPage)}?id={p.Id}");
                    break;
            }
        }
    }
}
