using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;
using Rss_feeder_prout.Helpers;
using Rss_feeder_prout.Models;
using Rss_feeder_prout.Services;

namespace Rss_feeder_prout.ViewModels
{
    /// <summary>NOUVEAU : statistiques de lecture, sources les plus actives, santé des flux, journal des synchros.</summary>
    public class StatsViewModel : BaseViewModel
    {
        private readonly SQLiteService _db;

        public ObservableCollection<NamedCount> TopSites { get; } = new();
        public ObservableCollection<NamedCount> PerDay { get; } = new();
        public ObservableCollection<SyncLogEntry> Logs { get; } = new();
        public ObservableCollection<FeedSite> BrokenSites { get; } = new();

        private string _total = "–", _unread = "–", _downloaded = "–", _favorites = "–", _archives = "–", _readRate = "–";
        public string Total { get => _total; set => SetProperty(ref _total, value); }
        public string Unread { get => _unread; set => SetProperty(ref _unread, value); }
        public string Downloaded { get => _downloaded; set => SetProperty(ref _downloaded, value); }
        public string Favorites { get => _favorites; set => SetProperty(ref _favorites, value); }
        public string Archives { get => _archives; set => SetProperty(ref _archives, value); }
        public string ReadRate { get => _readRate; set => SetProperty(ref _readRate, value); }

        private string _feedsHealth = string.Empty;
        public string FeedsHealth { get => _feedsHealth; set => SetProperty(ref _feedsHealth, value); }

        public bool HasBrokenSites => BrokenSites.Count > 0;

        public ICommand ClearLogsCommand { get; }
        public ICommand RefreshCommand { get; }

        public StatsViewModel(SQLiteService db)
        {
            _db = db;
            Title = "Statistiques";
            ClearLogsCommand = new Command(async () =>
            {
                if (!await UiHelper.ConfirmAsync("Journal", "Effacer le journal des synchronisations ?")) return;
                await _db.ClearSyncLogsAsync();
                Logs.Clear();
            });
            RefreshCommand = new Command(async () => await LoadAsync());
        }

        public async Task LoadAsync()
        {
            try
            {
                IsBusy = true;
                var counts = await _db.GetSmartCountsAsync();
                int archives = await _db.CountArchivesAsync();
                var top = await _db.GetTopSitesAsync(10);
                var perDay = await _db.GetArticlesPerDayAsync(7);
                var logs = await _db.GetSyncLogsAsync(40);
                var sites = await _db.GetSitesAsync();

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Total = counts.All.ToString("N0");
                    Unread = counts.Unread.ToString("N0");
                    Downloaded = counts.Downloaded.ToString("N0");
                    Favorites = counts.Favorites.ToString("N0");
                    Archives = archives.ToString("N0");
                    ReadRate = counts.All == 0 ? "–" : $"{100.0 * (counts.All - counts.Unread) / counts.All:0} %";

                    TopSites.Clear();
                    foreach (var t in top) TopSites.Add(t);

                    PerDay.Clear();
                    foreach (var d in perDay) PerDay.Add(d);

                    Logs.Clear();
                    foreach (var l in logs) Logs.Add(l);

                    BrokenSites.Clear();
                    foreach (var s in sites.Where(s => s.HasError).OrderByDescending(s => s.ErrorCount)) BrokenSites.Add(s);

                    int enabled = sites.Count(s => !s.IsDisabled);
                    FeedsHealth = $"{enabled - BrokenSites.Count}/{enabled} sources actives fonctionnent" +
                                  (sites.Count - enabled > 0 ? $" · {sites.Count - enabled} en pause" : string.Empty);
                    OnPropertyChanged(nameof(HasBrokenSites));
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[STATS] {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
