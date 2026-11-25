using Microsoft.Maui.ApplicationModel;
using Rss_feeder_prout.Models;
using Rss_feeder_prout.Services;
using System.Diagnostics;
using System.Windows.Input;

namespace Rss_feeder_prout.ViewModels
{
    /// <summary>
    /// Page web d'origine affichée DANS l'application (pour les sites incompatibles avec le mode lecture).
    /// Paramètres : url + title, ou itemId (ancien fonctionnement conservé).
    /// </summary>
    [QueryProperty(nameof(ItemId), "itemId")]
    [QueryProperty(nameof(Url), "url")]
    [QueryProperty(nameof(PageTitle), "title")]
    public class ItemDetailViewModel : BaseViewModel
    {
        private readonly SQLiteService _dbService;
        private readonly RssService _rssService;

        private int _itemId;
        public int ItemId
        {
            get => _itemId;
            set
            {
                if (SetProperty(ref _itemId, value) && value > 0)
                    _ = LoadItemAsync();
            }
        }

        private string _url = string.Empty;
        public string Url
        {
            get => _url;
            set
            {
                if (SetProperty(ref _url, value ?? string.Empty))
                    OnPropertyChanged(nameof(Host));
            }
        }

        private string _pageTitle = string.Empty;
        public string PageTitle
        {
            get => _pageTitle;
            set
            {
                if (SetProperty(ref _pageTitle, value ?? string.Empty))
                    Title = _pageTitle;
            }
        }

        public string Host => Uri.TryCreate(Url, UriKind.Absolute, out var u) ? u.Host : Url;

        private double _progress;
        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        private bool _isLoadingPage = true;
        public bool IsLoadingPage
        {
            get => _isLoadingPage;
            set => SetProperty(ref _isLoadingPage, value);
        }

        private RssItem _rssItem = new();
        public RssItem RssItem
        {
            get => _rssItem;
            set => SetProperty(ref _rssItem, value);
        }

        public string DisplayContent => RssItem?.ContentHtml ?? RssItem?.Summary ?? string.Empty;
        public bool IsContentDownloaded => !string.IsNullOrWhiteSpace(RssItem?.ContentHtml);

        public ICommand OpenExternalCommand { get; }
        public ICommand DownloadContentCommand { get; }
        public ICommand ShareCommand { get; }

        public ItemDetailViewModel(SQLiteService dbService, RssService rssService)
        {
            _dbService = dbService;
            _rssService = rssService;

            OpenExternalCommand = new Command(async () => await ExecuteOpenExternalCommand());
            DownloadContentCommand = new Command(async () => await ExecuteDownloadContentCommand());
            ShareCommand = new Command(async () =>
            {
                if (!string.IsNullOrWhiteSpace(Url))
                    await Share.Default.RequestAsync(new ShareTextRequest { Uri = Url, Title = Title });
            });
        }

        private async Task LoadItemAsync()
        {
            try
            {
                IsBusy = true;
                var item = await _dbService.GetRssItemAsync(ItemId);
                if (item != null)
                {
                    RssItem = item;
                    Url = item.Link;
                    PageTitle = item.Title;
                    if (!item.IsRead) await _dbService.MarkItemAsReadAsync(item);
                    OnPropertyChanged(nameof(DisplayContent));
                    OnPropertyChanged(nameof(IsContentDownloaded));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load RSS Item: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ExecuteDownloadContentCommand()
        {
            if (RssItem == null || RssItem.Id == 0 || RssItem.IsDownloaded) return;
            try
            {
                RssItem.IsBusy = true;
                await _rssService.DownloadAndSaveItemContentAsync(RssItem);
                OnPropertyChanged(nameof(DisplayContent));
                OnPropertyChanged(nameof(IsContentDownloaded));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error downloading content: {ex.Message}");
            }
            finally
            {
                RssItem.IsBusy = false;
            }
        }

        private async Task ExecuteOpenExternalCommand()
        {
            await UiHelper.OpenBrowserAsync(Url);
        }
    }
}
