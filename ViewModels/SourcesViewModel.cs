using Rss_feeder_prout.Models;
using Rss_feeder_prout.Services;
using Rss_feeder_prout.Views;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;

namespace Rss_feeder_prout.ViewModels
{
    /// <summary>Sources d'une catégorie (grille) avec compteurs de non-lus et état de santé.</summary>
    [QueryProperty(nameof(PlaylistIdText), "playlistId")]
    public class SourcesViewModel : BaseViewModel
    {
        private readonly SQLiteService _db;
        private readonly RssService _rss;

        public SyncService Sync { get; }

        public ObservableCollection<FeedSite> Sites { get; } = new();

        private FeedPlaylist? _playlist;
        public FeedPlaylist? Playlist
        {
            get => _playlist;
            set => SetProperty(ref _playlist, value);
        }

        private string _playlistIdText = string.Empty;
        public string PlaylistIdText
        {
            get => _playlistIdText;
            set
            {
                if (SetProperty(ref _playlistIdText, value))
                    _ = LoadAsync();
            }
        }

        private int _unreadTotal;
        public int UnreadTotal
        {
            get => _unreadTotal;
            set
            {
                if (SetProperty(ref _unreadTotal, value))
                    OnPropertyChanged(nameof(AllSubtitle));
            }
        }

        private int _total;
        public int Total
        {
            get => _total;
            set
            {
                if (SetProperty(ref _total, value))
                    OnPropertyChanged(nameof(AllSubtitle));
            }
        }

        public string AllSubtitle => $"{Total} articles · {UnreadTotal} non lus";

        public ICommand OpenSiteCommand { get; }
        public ICommand ShowAllCommand { get; }
        public ICommand ShowUnreadCommand { get; }
        public ICommand SyncPlaylistCommand { get; }
        public ICommand ManageCommand { get; }
        public ICommand MarkAllReadCommand { get; }
        public ICommand SiteMenuCommand { get; }
        public ICommand RefreshCommand { get; }

        private bool _isRefreshing;
        public bool IsRefreshing
        {
            get => _isRefreshing;
            set => SetProperty(ref _isRefreshing, value);
        }

        public SourcesViewModel(SQLiteService db, RssService rss, SyncService sync)
        {
            _db = db;
            _rss = rss;
            Sync = sync;

            OpenSiteCommand = new Command<FeedSite>(async s => await OpenSiteAsync(s));
            ShowAllCommand = new Command(async () => await OpenListAsync(null, false));
            ShowUnreadCommand = new Command(async () => await OpenListAsync(null, true));
            SyncPlaylistCommand = new Command(async () => await SyncAsync());
            ManageCommand = new Command(async () =>
            {
                if (Playlist != null) await UiHelper.GoToAsync($"{nameof(PlaylistDetailPage)}?id={Playlist.Id}");
            });
            MarkAllReadCommand = new Command(async () => await MarkAllReadAsync());
            SiteMenuCommand = new Command<FeedSite>(async s => await SiteMenuAsync(s));
            RefreshCommand = new Command(async () =>
            {
                IsRefreshing = true;
                await LoadAsync();
                IsRefreshing = false;
            });

        }

        public async Task LoadAsync()
        {
            if (!int.TryParse(PlaylistIdText, out var id)) return;
            try
            {
                Playlist = await _db.GetPlaylistAsync(id);
                if (Playlist == null) return;
                Title = Playlist.Name;

                var sites = await _db.GetSitesForPlaylistAsync(id);
                var unread = await _db.GetUnreadCountsBySiteAsync(id);
                var totals = await _db.GetTotalCountsBySiteAsync();

                foreach (var s in sites)
                {
                    s.UnreadCount = unread.TryGetValue(s.Id, out var u) ? u : 0;
                    s.TotalCount = totals.TryGetValue(s.Id, out var t) ? t : 0;
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Sites.Clear();
                    foreach (var s in sites) Sites.Add(s);
                    UnreadTotal = sites.Sum(s => s.UnreadCount);
                    Total = sites.Sum(s => s.TotalCount);
                    Subtitle = $"{sites.Count} sources";
                });

                // Icônes manquantes en arrière-plan
                _ = Task.Run(async () =>
                {
                    foreach (var s in sites.Where(x => !x.HasIcon).Take(30))
                    {
                        try { await _rss.EnsureSiteIconAsync(s); } catch { }
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SOURCES] {ex.Message}");
            }
        }

        private async Task OpenSiteAsync(FeedSite? site)
        {
            if (site == null) return;
            await UiHelper.GoToAsync($"{nameof(ArticleListPage)}?siteId={site.Id}&title={Uri.EscapeDataString(site.DisplayName)}");
        }

        private async Task OpenListAsync(int? siteId, bool unread)
        {
            if (Playlist == null) return;
            var route = $"{nameof(ArticleListPage)}?playlistId={Playlist.Id}&title={Uri.EscapeDataString(Playlist.Name)}";
            if (unread) route += "&smart=Unread";
            await UiHelper.GoToAsync(route);
        }

        private async Task SyncAsync()
        {
            if (Playlist == null) return;
            if (Sync.IsRunning) { UiHelper.Toast("Une synchronisation est déjà en cours"); return; }

            var options = await SyncOptionsPage.AskAsync();
            if (options == null) return;
            options.PlaylistIds = new[] { Playlist.Id };
            options.OnlyActivePlaylists = false;
            options.Label = $"Synchro · {Playlist.Name}";

            _ = Task.Run(async () =>
            {
                var r = await Sync.RunAsync(options);
                UiHelper.Toast(r.Summary);
            });
        }

        private async Task MarkAllReadAsync()
        {
            if (Playlist == null) return;
            if (!await UiHelper.ConfirmAsync("Tout marquer comme lu", $"Marquer tous les articles de « {Playlist.Name} » comme lus ?")) return;
            await _db.MarkAllReadAsync(new ArticleQuery { PlaylistId = Playlist.Id });
            await LoadAsync();
        }

        private async Task SiteMenuAsync(FeedSite? site)
        {
            if (site == null) return;
            var choice = await UiHelper.ActionSheetAsync(site.DisplayName, "Annuler", null,
                "Voir les articles", "Tester le flux", "Tout marquer comme lu", "Ouvrir le site", "Gérer les sources");
            switch (choice)
            {
                case "Voir les articles": await OpenSiteAsync(site); break;
                case "Tester le flux":
                    site.IsBusy = true;
                    var (ok, msg) = await _rss.TestFeedAsync(site);
                    site.IsBusy = false;
                    site.RefreshHealth();
                    await UiHelper.AlertAsync(ok ? "Flux OK" : "Problème", msg);
                    break;
                case "Tout marquer comme lu":
                    await _db.MarkAllReadAsync(new ArticleQuery { SiteId = site.Id });
                    await LoadAsync();
                    break;
                case "Ouvrir le site":
                    await UiHelper.OpenBrowserAsync(string.IsNullOrWhiteSpace(site.SiteUrl) ? site.FeedUrl : site.SiteUrl);
                    break;
                case "Gérer les sources":
                    ManageCommand.Execute(null);
                    break;
            }
        }
    }
}
