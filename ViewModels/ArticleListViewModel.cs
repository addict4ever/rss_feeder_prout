using Rss_feeder_prout.Helpers;
using Rss_feeder_prout.Models;
using Rss_feeder_prout.Services;
using Rss_feeder_prout.Views;
using System.Diagnostics;
using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Networking;

namespace Rss_feeder_prout.ViewModels
{
    /// <summary>
    /// Liste d'articles d'une catégorie, d'une source ou d'un flux intelligent (Tous, Non lus, Aujourd'hui…).
    /// Mélange les articles ENREGISTRÉS et (option) les articles EN LIGNE pas encore dans la base.
    /// Paramètres de navigation : playlistId, siteId, smart, title.
    /// </summary>
    public class ArticleListViewModel : ArticleListViewModelBase, IQueryAttributable
    {
        private const int PageSize = 150;

        private int? _playlistId;
        private int? _siteId;
        private SmartFeed _smart = SmartFeed.None;
        private int _offset;
        private bool _hasMore;
        private bool _loadingMore;
        private bool _loaded;
        private CancellationTokenSource? _liveCts;

        public ArticleListViewModel(SQLiteService db, RssService rss, ReaderSession session, SyncService sync)
            : base(db, rss, session, sync)
        {
            FilterCommand = new Command<string>(f => { CurrentFilter = f; _ = ReloadAsync(); });
            SortCommand = new Command(async () => await ChooseSortAsync());
            ToggleSearchCommand = new Command(() => { IsSearchVisible = !IsSearchVisible; if (!IsSearchVisible) SearchText = string.Empty; });
            ToggleOnlineCommand = new Command(async () => { ShowOnline = !ShowOnline; await ReloadAsync(); });
            RefreshCommand = new Command(async () => await RefreshAsync());
            LoadMoreCommand = new Command(async () => await LoadMoreAsync());
            MenuCommand = new Command(async () => await MenuAsync());
            MarkAllAsReadCommand = new Command(async () => await MarkAllAsReadAsync());
            SyncHereCommand = new Command(async () => await SyncHereAsync());
            SaveAllOnlineCommand = new Command(async () => await SaveAllOnlineAsync());

            _showOnline = AppSettings.ShowOnlineByDefault;
        }

        // ------------------------------------------------------------------
        // Paramètres
        // ------------------------------------------------------------------

        public void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            _playlistId = query.TryGetValue("playlistId", out var p) && int.TryParse(p?.ToString(), out var pid) ? pid : null;
            _siteId = query.TryGetValue("siteId", out var s) && int.TryParse(s?.ToString(), out var sid) ? sid : null;
            _smart = query.TryGetValue("smart", out var sm) && Enum.TryParse<SmartFeed>(sm?.ToString(), out var smart) ? smart : SmartFeed.None;
            Title = query.TryGetValue("title", out var t) ? Uri.UnescapeDataString(t?.ToString() ?? string.Empty) : "Articles";

            // Les flux intelligents n'affichent pas les articles en ligne par défaut (trop de sources)
            if (_smart != SmartFeed.None) _showOnline = false;
            if (_smart == SmartFeed.Unread) CurrentFilter = "Unread";

            OnPropertyChanged(nameof(ShowOnline));
            OnPropertyChanged(nameof(CanShowOnline));
            _loaded = false;
        }

        // ------------------------------------------------------------------
        // État UI
        // ------------------------------------------------------------------

        private string _currentFilter = "All";
        public string CurrentFilter
        {
            get => _currentFilter;
            set
            {
                if (SetProperty(ref _currentFilter, value))
                    Notify(nameof(IsFilterAll), nameof(IsFilterUnread), nameof(IsFilterRead), nameof(IsFilterOffline), nameof(IsFilterFavorites));
            }
        }

        public bool IsFilterAll => CurrentFilter == "All";
        public bool IsFilterUnread => CurrentFilter == "Unread";
        public bool IsFilterRead => CurrentFilter == "Read";
        public bool IsFilterOffline => CurrentFilter == "Offline";
        public bool IsFilterFavorites => CurrentFilter == "Favorites";

        private ArticleSort _sort = ArticleSort.Newest;
        public ArticleSort Sort
        {
            get => _sort;
            set
            {
                if (SetProperty(ref _sort, value))
                    OnPropertyChanged(nameof(SortText));
            }
        }

        public string SortText => Sort switch
        {
            ArticleSort.Oldest => "Plus anciens",
            ArticleSort.BySite => "Par source",
            _ => "Plus récents"
        };

        private bool _isSearchVisible;
        public bool IsSearchVisible
        {
            get => _isSearchVisible;
            set => SetProperty(ref _isSearchVisible, value);
        }

        private string _searchText = string.Empty;
        private CancellationTokenSource? _searchDebounce;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value ?? string.Empty))
                    DebounceReload();
            }
        }

        private bool _showOnline;
        /// <summary>Afficher aussi les articles en ligne non enregistrés.</summary>
        public bool ShowOnline
        {
            get => _showOnline;
            set => SetProperty(ref _showOnline, value);
        }

        public bool CanShowOnline => true;

        private bool _isLoadingOnline;
        public bool IsLoadingOnline
        {
            get => _isLoadingOnline;
            set => SetProperty(ref _isLoadingOnline, value);
        }

        private int _onlineCount;
        public int OnlineCount
        {
            get => _onlineCount;
            set
            {
                if (SetProperty(ref _onlineCount, value))
                    Notify(nameof(HasOnline), nameof(OnlineText));
            }
        }

        public bool HasOnline => OnlineCount > 0;
        public string OnlineText => OnlineCount == 1 ? "1 article en ligne pas encore enregistré" : $"{OnlineCount} articles en ligne pas encore enregistrés";

        // ------------------------------------------------------------------
        // Commandes
        // ------------------------------------------------------------------

        public ICommand FilterCommand { get; }
        public ICommand SortCommand { get; }
        public ICommand ToggleSearchCommand { get; }
        public ICommand ToggleOnlineCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand LoadMoreCommand { get; }
        public ICommand MenuCommand { get; }
        public ICommand MarkAllAsReadCommand { get; }
        public ICommand SyncHereCommand { get; }
        public ICommand SaveAllOnlineCommand { get; }

        // ------------------------------------------------------------------
        // Chargement
        // ------------------------------------------------------------------

        public async Task OnAppearingAsync()
        {
            Attach();
            if (!_loaded)
            {
                _loaded = true;
                await ReloadAsync();
            }
            else
            {
                // Retour du lecteur : on rafraîchit simplement l'affichage (lu, favori…)
                foreach (var i in Items) i.RefreshDisplay();
                UpdateCountText();
            }
        }

        private ArticleQuery BuildQuery()
        {
            var q = new ArticleQuery
            {
                PlaylistId = _playlistId,
                SiteId = _siteId,
                Sort = Sort,
                Search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
                Limit = PageSize,
                Offset = _offset
            };

            q.Filter = CurrentFilter switch
            {
                "Unread" => ArticleFilter.Unread,
                "Read" => ArticleFilter.Read,
                "Offline" => ArticleFilter.Downloaded,
                "Favorites" => ArticleFilter.Favorites,
                _ => ArticleFilter.All
            };

            switch (_smart)
            {
                case SmartFeed.Unread: if (q.Filter == ArticleFilter.All) q.Filter = ArticleFilter.Unread; break;
                case SmartFeed.Today: q.SinceUtc = DateTime.Today.ToUniversalTime(); break;
                case SmartFeed.Favorites: q.Filter = ArticleFilter.Favorites; break;
                case SmartFeed.ReadLater: q.Filter = ArticleFilter.ReadLater; break;
                case SmartFeed.Downloaded: q.Filter = ArticleFilter.Downloaded; break;
            }

            return q;
        }

        private void DebounceReload()
        {
            _searchDebounce?.Cancel();
            var cts = new CancellationTokenSource();
            _searchDebounce = cts;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(350, cts.Token);
                    await MainThread.InvokeOnMainThreadAsync(ReloadAsync);
                }
                catch (OperationCanceledException) { }
            });
        }

        public async Task ReloadAsync()
        {
            if (IsBusy) return;
            try
            {
                IsBusy = true;
                _offset = 0;
                await LoadSiteMapAsync();

                var stored = await Db.QueryArticlesAsync(BuildQuery());
                _hasMore = stored.Count >= PageSize;
                _offset = stored.Count;

                var list = Decorate(stored);
                ReplaceItems(list);
                UpdateEmptyTexts();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LIST] {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }

            if (ShowOnline) await LoadOnlineAsync();
            else OnlineCount = 0;
        }

        /// <summary>Ajoute les articles en ligne (non enregistrés) en tête de liste.</summary>
        private async Task LoadOnlineAsync()
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet) { OnlineCount = 0; return; }

            // Les articles en ligne n'ont pas de sens pour ces filtres
            if (CurrentFilter is "Read" or "Offline" or "Favorites" || _smart is SmartFeed.Favorites or SmartFeed.ReadLater or SmartFeed.Downloaded)
            {
                OnlineCount = 0;
                return;
            }

            _liveCts?.Cancel();
            _liveCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var ct = _liveCts.Token;

            try
            {
                IsLoadingOnline = true;
                var sites = SiteMap.Values.Where(s =>
                    (_siteId.HasValue && s.Id == _siteId.Value) ||
                    (!_siteId.HasValue && _playlistId.HasValue && s.PlaylistId == _playlistId.Value) ||
                    (!_siteId.HasValue && !_playlistId.HasValue)).ToList();

                var live = await Rss.FetchLiveItemsAsync(sites, ct);
                if (ct.IsCancellationRequested) return;

                if (_smart == SmartFeed.Today)
                {
                    var since = DateTime.Today.ToUniversalTime();
                    live = live.Where(i => i.PublishDateUtc >= since).ToList();
                }

                if (!string.IsNullOrWhiteSpace(SearchText))
                {
                    var terms = HtmlHelper.NormalizeForSearch(SearchText).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    live = live.Where(i => terms.All(t => i.SearchBlob.Contains(t))).ToList();
                }

                var decorated = Decorate(live);
                OnlineCount = decorated.Count;
                if (decorated.Count == 0) return;

                // Fusion triée : en ligne + enregistrés
                var merged = Items.Concat(decorated);
                merged = Sort switch
                {
                    ArticleSort.Oldest => merged.OrderBy(i => i.PublishDateUtc),
                    ArticleSort.BySite => merged.OrderBy(i => i.SiteName).ThenByDescending(i => i.PublishDateUtc),
                    _ => merged.OrderByDescending(i => i.PublishDateUtc)
                };
                ReplaceItems(merged.ToList());
                UpdateEmptyTexts();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LIVE] {ex.Message}");
            }
            finally
            {
                IsLoadingOnline = false;
            }
        }

        private async Task LoadMoreAsync()
        {
            if (!_hasMore || _loadingMore || IsBusy) return;
            try
            {
                _loadingMore = true;
                var more = await Db.QueryArticlesAsync(BuildQuery());
                _hasMore = more.Count >= PageSize;
                _offset += more.Count;
                foreach (var i in Decorate(more)) Items.Add(i);
                UpdateCountText();
            }
            finally
            {
                _loadingMore = false;
            }
        }

        private async Task RefreshAsync()
        {
            try
            {
                IsRefreshing = true;
                await ReloadAsync();
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        private void UpdateEmptyTexts()
        {
            if (Items.Count > 0) return;
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                EmptyTitle = "Aucun résultat";
                EmptyText = $"Rien ne correspond à « {SearchText} ».";
            }
            else if (CurrentFilter == "Unread")
            {
                EmptyTitle = "Tout est lu 🎉";
                EmptyText = "Aucun article non lu ici.";
            }
            else
            {
                EmptyTitle = "Aucun article";
                EmptyText = Connectivity.Current.NetworkAccess == NetworkAccess.Internet
                    ? "Tirez vers le bas pour voir les articles en ligne, ou lancez une synchronisation."
                    : "Vous êtes hors connexion et rien n'est encore enregistré ici.";
            }
        }

        // ------------------------------------------------------------------
        // Actions
        // ------------------------------------------------------------------

        private async Task ChooseSortAsync()
        {
            var choice = await UiHelper.ActionSheetAsync("Trier", "Annuler", null, "Plus récents", "Plus anciens", "Par source");
            if (choice == null) return;
            Sort = choice switch { "Plus anciens" => ArticleSort.Oldest, "Par source" => ArticleSort.BySite, _ => ArticleSort.Newest };
            await ReloadAsync();
        }

        private ArticleQuery ScopeQuery()
        {
            var q = BuildQuery();
            q.Limit = int.MaxValue;
            q.Offset = 0;
            q.Search = null;
            return q;
        }

        /// <summary>Conservé : marquer tous les articles affichés comme lus.</summary>
        private async Task MarkAllAsReadAsync()
        {
            if (Items.Count == 0) return;
            if (!await UiHelper.ConfirmAsync("Lecture", "Marquer tous ces articles comme lus ?", "Oui", "Non")) return;

            var q = ScopeQuery();
            if (q.Filter == ArticleFilter.Unread) q.Filter = ArticleFilter.All;
            await Db.MarkAllReadAsync(q);
            foreach (var i in Items) i.IsRead = true;
            UpdateCountText();
            if (CurrentFilter == "Unread") await ReloadAsync();
        }

        private async Task SyncHereAsync()
        {
            if (Sync.IsRunning) { UiHelper.Toast("Une synchronisation est déjà en cours"); return; }

            var options = await SyncOptionsPage.AskAsync();
            if (options == null) return;

            IReadOnlyList<int>? ids = null;
            if (_playlistId.HasValue) ids = new[] { _playlistId.Value };
            else if (_siteId.HasValue && SiteMap.TryGetValue(_siteId.Value, out var site)) ids = new[] { site.PlaylistId };

            options.PlaylistIds = ids;
            options.OnlyActivePlaylists = ids == null;
            options.Label = $"Synchro · {Title}";

            _ = Task.Run(async () =>
            {
                var result = await Sync.RunAsync(options);
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    UiHelper.Toast(result.Summary);
                    await ReloadAsync();
                });
            });
        }

        private async Task SaveAllOnlineAsync()
        {
            var live = Items.Where(i => i.IsLive).ToList();
            if (live.Count == 0) return;

            var choice = await UiHelper.ActionSheetAsync($"Enregistrer {live.Count} articles en ligne", "Annuler", null,
                "Résumés seulement (rapide)", "Articles complets (hors-ligne)");
            if (choice == null) return;
            bool full = choice.StartsWith("Articles complets");

            IsBusy = true;
            try
            {
                int n = 0;
                foreach (var item in live)
                {
                    item.IsBusy = true;
                    try
                    {
                        if (await Task.Run(() => Rss.SaveLiveItemAsync(item, full, AppSettings.ManualImageMode)) != null) n++;
                    }
                    catch (Exception ex) { Debug.WriteLine($"[SAVE LIVE] {ex.Message}"); }
                    finally { item.IsBusy = false; }
                }
                OnlineCount = Items.Count(i => i.IsLive);
                UpdateCountText();
                UiHelper.Toast($"{n} articles enregistrés");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task MenuAsync()
        {
            var actions = new List<string> { "Tout marquer comme lu", "Synchroniser (enregistrer)" };
            if (Items.Any(i => i.IsLive)) actions.Add("Enregistrer les articles en ligne");
            actions.Add("Télécharger tout hors-ligne");
            actions.Add(AppSettings.CompactList ? "Affichage détaillé" : "Affichage compact");
            actions.Add(AppSettings.ShowThumbnails ? "Masquer les vignettes" : "Afficher les vignettes");
            if (_siteId.HasValue) actions.Add("Gérer cette source");

            var choice = await UiHelper.ActionSheetAsync(Title, "Annuler", null, actions.ToArray());
            switch (choice)
            {
                case "Tout marquer comme lu": await MarkAllAsReadAsync(); break;
                case "Synchroniser (enregistrer)": await SyncHereAsync(); break;
                case "Enregistrer les articles en ligne": await SaveAllOnlineAsync(); break;
                case "Télécharger tout hors-ligne": await DownloadAllAsync(); break;
                case "Affichage détaillé":
                case "Affichage compact":
                    AppSettings.CompactList = !AppSettings.CompactList;
                    foreach (var i in Items) i.RefreshDisplay();
                    break;
                case "Masquer les vignettes":
                case "Afficher les vignettes":
                    AppSettings.ShowThumbnails = !AppSettings.ShowThumbnails;
                    foreach (var i in Items) i.RefreshDisplay();
                    break;
                case "Gérer cette source":
                    if (_siteId.HasValue && SiteMap.TryGetValue(_siteId.Value, out var site))
                        await UiHelper.GoToAsync($"{nameof(PlaylistDetailPage)}?id={site.PlaylistId}");
                    break;
            }
        }

        /// <summary>Télécharge en arrière-plan le contenu complet des articles affichés.</summary>
        private async Task DownloadAllAsync()
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                await UiHelper.AlertAsync("Action impossible", "Le téléchargement nécessite une connexion.");
                return;
            }

            var targets = Items.Where(i => i.IsStored && (!i.IsDownloaded || (!i.ImagesCached && AppSettings.ManualImageMode != ImageMode.None))).ToList();
            if (targets.Count == 0) { UiHelper.Toast("Tout est déjà hors-ligne"); return; }

            UiHelper.Toast($"Téléchargement de {targets.Count} articles…");
            _ = Task.Run(async () =>
            {
                int ok = 0;
                using var gate = new SemaphoreSlim(3);
                var tasks = targets.Select(async item =>
                {
                    await gate.WaitAsync();
                    try
                    {
                        MainThread.BeginInvokeOnMainThread(() => item.IsBusy = true);
                        if (await Rss.DownloadAndSaveItemContentAsync(item, AppSettings.ManualImageMode)) Interlocked.Increment(ref ok);
                    }
                    catch { }
                    finally
                    {
                        MainThread.BeginInvokeOnMainThread(() => item.IsBusy = false);
                        gate.Release();
                    }
                }).ToList();
                await Task.WhenAll(tasks);
                UiHelper.Toast($"{ok}/{targets.Count} articles disponibles hors-ligne");
            });
        }

        public override void Detach()
        {
            base.Detach();
            _liveCts?.Cancel();
        }
    }
}
