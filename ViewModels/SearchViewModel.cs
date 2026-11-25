using Rss_feeder_prout.Models;
using Rss_feeder_prout.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;

namespace Rss_feeder_prout.ViewModels
{
    /// <summary>
    /// NOUVEAU : recherche plein texte dans TOUS les articles (titre, résumé, contenu téléchargé, source, auteur),
    /// insensible aux accents, avec filtres de période / état et historique des recherches.
    /// </summary>
    public class SearchViewModel : ArticleListViewModelBase
    {
        private CancellationTokenSource? _debounce;

        public ObservableCollection<string> RecentSearches { get; } = new();

        public ICommand SearchCommand { get; }
        public ICommand PeriodCommand { get; }
        public ICommand UseRecentCommand { get; }
        public ICommand ClearRecentCommand { get; }
        public ICommand ToggleUnreadCommand { get; }
        public ICommand ToggleOfflineCommand { get; }
        public ICommand ToggleTitleOnlyCommand { get; }

        public SearchViewModel(SQLiteService db, RssService rss, ReaderSession session, SyncService sync)
            : base(db, rss, session, sync)
        {
            Title = "Recherche";
            SearchCommand = new Command(async () => await SearchAsync(true));
            PeriodCommand = new Command<string>(async p => { Period = p; await SearchAsync(false); });
            UseRecentCommand = new Command<string>(term => SearchText = term);
            ClearRecentCommand = new Command(() => { AppSettings.RecentSearches = string.Empty; LoadRecent(); });
            ToggleUnreadCommand = new Command(async () => { UnreadOnly = !UnreadOnly; await SearchAsync(false); });
            ToggleOfflineCommand = new Command(async () => { OfflineOnly = !OfflineOnly; await SearchAsync(false); });
            ToggleTitleOnlyCommand = new Command(async () => { TitleOnly = !TitleOnly; await SearchAsync(false); });
            EmptyTitle = "Rechercher";
            EmptyText = "Titre, résumé, contenu, source… La recherche ignore les accents et les majuscules.";
            LoadRecent();
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(HasQuery));
                    Debounce();
                }
            }
        }

        public bool HasQuery => !string.IsNullOrWhiteSpace(SearchText);

        private string _period = "All";
        public string Period
        {
            get => _period;
            set
            {
                if (SetProperty(ref _period, value))
                    Notify(nameof(IsPeriodAll), nameof(IsPeriodDay), nameof(IsPeriodWeek), nameof(IsPeriodMonth));
            }
        }

        public bool IsPeriodAll => Period == "All";
        public bool IsPeriodDay => Period == "Day";
        public bool IsPeriodWeek => Period == "Week";
        public bool IsPeriodMonth => Period == "Month";

        private bool _unreadOnly;
        public bool UnreadOnly { get => _unreadOnly; set => SetProperty(ref _unreadOnly, value); }

        private bool _offlineOnly;
        public bool OfflineOnly { get => _offlineOnly; set => SetProperty(ref _offlineOnly, value); }

        private bool _titleOnly;
        public bool TitleOnly { get => _titleOnly; set => SetProperty(ref _titleOnly, value); }

        public bool ShowRecent => !HasQuery && RecentSearches.Count > 0;

        private void LoadRecent()
        {
            RecentSearches.Clear();
            foreach (var r in AppSettings.GetRecentSearches()) RecentSearches.Add(r);
            OnPropertyChanged(nameof(ShowRecent));
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
                    await Task.Delay(400, cts.Token);
                    await MainThread.InvokeOnMainThreadAsync(() => SearchAsync(false));
                }
                catch (OperationCanceledException) { }
            });
        }

        public async Task SearchAsync(bool remember)
        {
            OnPropertyChanged(nameof(ShowRecent));
            if (!HasQuery)
            {
                Items.Clear();
                CountText = string.Empty;
                EmptyTitle = "Rechercher";
                EmptyText = "Titre, résumé, contenu, source… La recherche ignore les accents et les majuscules.";
                return;
            }

            try
            {
                IsBusy = true;
                if (SiteMap.Count == 0) await LoadSiteMapAsync();

                var q = new ArticleQuery
                {
                    Search = SearchText,
                    SearchTitleOnly = TitleOnly,
                    Limit = 300,
                    Filter = UnreadOnly ? ArticleFilter.Unread : (OfflineOnly ? ArticleFilter.Downloaded : ArticleFilter.All),
                    SinceUtc = Period switch
                    {
                        "Day" => DateTime.UtcNow.AddDays(-1),
                        "Week" => DateTime.UtcNow.AddDays(-7),
                        "Month" => DateTime.UtcNow.AddDays(-30),
                        _ => null
                    }
                };

                var results = await Db.QueryArticlesAsync(q);
                if (UnreadOnly && OfflineOnly) results = results.Where(r => r.IsDownloaded).ToList();

                ReplaceItems(Decorate(results));
                EmptyTitle = "Aucun résultat";
                EmptyText = $"Rien ne correspond à « {SearchText} ».";

                if (remember)
                {
                    AppSettings.AddRecentSearch(SearchText);
                    LoadRecent();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SEARCH] {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task OnAppearingAsync()
        {
            Attach();
            await LoadSiteMapAsync();
            if (HasQuery) await SearchAsync(false);
            else LoadRecent();
        }
    }
}
