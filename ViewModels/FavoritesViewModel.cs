using Rss_feeder_prout.Models;
using Rss_feeder_prout.Services;
using System.Diagnostics;
using System.Windows.Input;

namespace Rss_feeder_prout.ViewModels
{
    /// <summary>NOUVEAU : Favoris et « À lire plus tard ».</summary>
    public class FavoritesViewModel : ArticleListViewModelBase
    {
        private readonly BackupService _backup;

        public ICommand SegmentCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ExportCommand { get; }

        public FavoritesViewModel(SQLiteService db, RssService rss, ReaderSession session, SyncService sync, BackupService backup)
            : base(db, rss, session, sync)
        {
            _backup = backup;
            Title = "Favoris";
            SegmentCommand = new Command<string>(async s => { Segment = s; await LoadAsync(); });
            RefreshCommand = new Command(async () => { IsRefreshing = true; await LoadAsync(); IsRefreshing = false; });
            ExportCommand = new Command(async () => await ExportAsync());
        }

        private string _segment = "Favorites";
        public string Segment
        {
            get => _segment;
            set
            {
                if (SetProperty(ref _segment, value))
                    Notify(nameof(IsFavorites), nameof(IsReadLater));
            }
        }

        public bool IsFavorites => Segment == "Favorites";
        public bool IsReadLater => Segment == "ReadLater";

        public async Task LoadAsync()
        {
            try
            {
                IsBusy = true;
                await LoadSiteMapAsync();
                var items = await Db.QueryArticlesAsync(new ArticleQuery
                {
                    Filter = IsFavorites ? ArticleFilter.Favorites : ArticleFilter.ReadLater,
                    Limit = 1000
                });
                ReplaceItems(Decorate(items));

                EmptyTitle = IsFavorites ? "Aucun favori" : "Rien à lire plus tard";
                EmptyText = IsFavorites
                    ? "Glissez un article vers la gauche ou touchez l'étoile dans le lecteur pour l'ajouter ici."
                    : "Utilisez « Lire plus tard » dans le lecteur ou le menu d'un article.";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FAVORITES] {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ExportAsync()
        {
            if (Items.Count == 0) { UiHelper.Toast("Rien à exporter"); return; }

            var choice = await UiHelper.ActionSheetAsync("Exporter", "Annuler", null, "Page HTML lisible", "Fichier JSON");
            if (choice == null) return;

            try
            {
                IsBusy = true;
                string path;
                if (choice == "Fichier JSON")
                {
                    path = await _backup.ExportArticlesJsonAsync(new ArticleQuery
                    {
                        Filter = IsFavorites ? ArticleFilter.Favorites : ArticleFilter.ReadLater,
                        Limit = 5000
                    }, includeContent: true);
                }
                else
                {
                    var list = new List<(string, string, DateTime, string, string)>();
                    foreach (var i in Items)
                    {
                        var full = i.IsDownloaded ? await Db.GetItemByIdAsync(i.Id) : null;
                        list.Add((i.Title, i.SiteName, i.PublishedLocal, i.Link, full?.ContentHtml is { Length: > 0 } c ? c : $"<p>{Helpers.HtmlHelper.Encode(i.Summary)}</p>"));
                    }
                    path = await _backup.ExportHtmlAsync(IsFavorites ? "Mes favoris" : "À lire plus tard", list);
                }
                await BackupService.ShareFileAsync(path, "Export Rss Prout");
            }
            catch (Exception ex)
            {
                await UiHelper.AlertAsync("Erreur", ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task OnAppearingAsync()
        {
            Attach();
            await LoadAsync();
        }
    }
}
