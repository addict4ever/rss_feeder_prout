using Rss_feeder_prout.Helpers;
using Rss_feeder_prout.Models;
using Rss_feeder_prout.Services;
using Rss_feeder_prout.Views;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Networking;

namespace Rss_feeder_prout.ViewModels
{
    /// <summary>
    /// Base commune à toutes les listes d'articles (catégorie, source, recherche, favoris…) :
    /// ouverture, lu / non lu, favori, plus tard, enregistrement hors-ligne, partage, filtres mots-clés.
    /// </summary>
    public abstract class ArticleListViewModelBase : BaseViewModel
    {
        protected readonly SQLiteService Db;
        protected readonly RssService Rss;
        protected readonly ReaderSession Session;

        public SyncService Sync { get; }

        public ObservableCollection<RssItem> Items { get; } = new();

        protected Dictionary<int, FeedSite> SiteMap { get; private set; } = new();

        private bool _isRefreshing;
        public bool IsRefreshing
        {
            get => _isRefreshing;
            set => SetProperty(ref _isRefreshing, value);
        }

        private string _emptyTitle = "Aucun article";
        public string EmptyTitle
        {
            get => _emptyTitle;
            set => SetProperty(ref _emptyTitle, value);
        }

        private string _emptyText = "Rien à afficher pour le moment.";
        public string EmptyText
        {
            get => _emptyText;
            set => SetProperty(ref _emptyText, value);
        }

        private string _countText = string.Empty;
        public string CountText
        {
            get => _countText;
            set => SetProperty(ref _countText, value);
        }

        public ICommand OpenItemCommand { get; }
        public ICommand ToggleReadCommand { get; }
        public ICommand ToggleFavoriteCommand { get; }
        public ICommand ToggleReadLaterCommand { get; }
        public ICommand SaveOfflineCommand { get; }
        public ICommand ShareItemCommand { get; }
        public ICommand ItemMenuCommand { get; }

        protected ArticleListViewModelBase(SQLiteService db, RssService rss, ReaderSession session, SyncService sync)
        {
            Db = db;
            Rss = rss;
            Session = session;
            Sync = sync;

            OpenItemCommand = new Command<RssItem>(async item => await OpenItemAsync(item));
            ToggleReadCommand = new Command<RssItem>(async item => await ToggleReadAsync(item));
            ToggleFavoriteCommand = new Command<RssItem>(async item => await ToggleFavoriteAsync(item));
            ToggleReadLaterCommand = new Command<RssItem>(async item => await ToggleReadLaterAsync(item));
            SaveOfflineCommand = new Command<RssItem>(async item => await SaveOfflineAsync(item));
            ShareItemCommand = new Command<RssItem>(async item => await ShareAsync(item));
            ItemMenuCommand = new Command<RssItem>(async item => await ItemMenuAsync(item));
        }

        private bool _attached;

        /// <summary>À appeler dans OnAppearing de la page (abonnements aux événements).</summary>
        public virtual void Attach()
        {
            if (_attached) return;
            _attached = true;
            Rss.ItemContentDownloaded += OnItemContentDownloaded;
        }

        /// <summary>À appeler dans OnDisappearing de la page (évite les fuites mémoire).</summary>
        public virtual void Detach()
        {
            if (!_attached) return;
            _attached = false;
            Rss.ItemContentDownloaded -= OnItemContentDownloaded;
        }

        private void OnItemContentDownloaded(object? sender, RssItem downloaded)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var match = Items.FirstOrDefault(i => i.Id == downloaded.Id && i.Id > 0);
                if (match != null && !ReferenceEquals(match, downloaded))
                {
                    match.IsDownloaded = downloaded.IsDownloaded;
                    match.ImagesCached = downloaded.ImagesCached;
                    match.ReadingMinutes = downloaded.ReadingMinutes;
                    if (!string.IsNullOrEmpty(downloaded.LocalImagePath)) match.LocalImagePath = downloaded.LocalImagePath;
                }
            });
        }

        // ------------------------------------------------------------------
        // Chargement commun
        // ------------------------------------------------------------------

        protected async Task LoadSiteMapAsync()
        {
            var sites = await Db.GetSitesAsync();
            SiteMap = sites.ToDictionary(s => s.Id);
        }

        /// <summary>Applique icônes de site + filtres "mots-clés masqués / surveillés".</summary>
        protected List<RssItem> Decorate(IEnumerable<RssItem> items)
        {
            var muted = HtmlHelper.ParseKeywords(AppSettings.MutedKeywords);
            var watched = HtmlHelper.ParseKeywords(AppSettings.HighlightKeywords);
            var result = new List<RssItem>();

            foreach (var item in items)
            {
                var text = HtmlHelper.NormalizeForSearch(item.Title + " " + item.Summary);
                if (muted.Count > 0 && HtmlHelper.ContainsAny(text, muted)) continue;

                item.IsHighlighted = watched.Count > 0 && HtmlHelper.ContainsAny(text, watched);
                if (item.SiteId.HasValue && SiteMap.TryGetValue(item.SiteId.Value, out var site))
                {
                    item.SiteIconPath = site.LocalIconPath;
                    if (string.IsNullOrWhiteSpace(item.SiteName)) item.SiteName = site.Name;
                }
                result.Add(item);
            }
            return result;
        }

        protected void ReplaceItems(IEnumerable<RssItem> items)
        {
            Items.Clear();
            foreach (var i in items) Items.Add(i);
            UpdateCountText();
        }

        protected virtual void UpdateCountText()
        {
            int unread = Items.Count(i => !i.IsRead);
            int online = Items.Count(i => i.IsLive);
            var parts = new List<string> { FormatHelper.Plural(Items.Count, "article", "articles") };
            if (unread > 0) parts.Add(FormatHelper.Plural(unread, "non lu", "non lus"));
            if (online > 0) parts.Add($"{online} en ligne");
            CountText = string.Join(" · ", parts);
        }

        // ------------------------------------------------------------------
        // Actions sur un article
        // ------------------------------------------------------------------

        protected virtual async Task OpenItemAsync(RssItem? item)
        {
            if (item == null) return;

            if (string.IsNullOrWhiteSpace(item.Link) && !item.IsDownloaded)
            {
                await UiHelper.AlertAsync("Erreur", "Lien d'article introuvable.");
                return;
            }

            // Marquer comme lu immédiatement (sans attendre la navigation)
            if (!item.IsRead && AppSettings.MarkReadOnOpen)
            {
                if (item.IsStored)
                    _ = Db.SetReadAsync(item, true);
                else
                    item.IsRead = true;
                UpdateCountText();
            }

            // Sites incompatibles avec le mode lecture / préférence "navigateur"
            bool browser = AppSettings.OpenMode == 1;
            if (item.SiteId.HasValue && SiteMap.TryGetValue(item.SiteId.Value, out var site) && site.OpenInBrowser) browser = true;
            if (item.Link.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || item.Link.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) browser = true;

            if (browser)
            {
                await OpenExternalLink(item);
                return;
            }

            Session.SetList(Items, item);
            await UiHelper.GoToAsync(nameof(ArticleDetailPage), new Dictionary<string, object> { ["Item"] = item });
        }

        /// <summary>Conservé : ouverture dans le navigateur interne (Custom Tabs).</summary>
        protected async Task OpenExternalLink(RssItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Link)) return;
            await UiHelper.OpenBrowserAsync(item.Link);
        }

        protected async Task ToggleReadAsync(RssItem? item)
        {
            if (item == null) return;
            await Db.SetReadAsync(item, !item.IsRead);
            UpdateCountText();
        }

        protected async Task ToggleFavoriteAsync(RssItem? item)
        {
            if (item == null) return;

            // Article en ligne : on l'enregistre d'abord
            if (!item.IsStored)
            {
                item.IsFavorite = true;
                await Task.Run(() => Rss.SaveLiveItemAsync(item, AppSettings.ManualDownloadContent, AppSettings.ManualImageMode));
                UiHelper.Toast("Enregistré et ajouté aux favoris");
                return;
            }

            await Db.SetFavoriteAsync(item, !item.IsFavorite);
            UiHelper.Toast(item.IsFavorite ? "Ajouté aux favoris" : "Retiré des favoris");
        }

        protected async Task ToggleReadLaterAsync(RssItem? item)
        {
            if (item == null) return;

            if (!item.IsStored)
            {
                item.IsReadLater = true;
                await Task.Run(() => Rss.SaveLiveItemAsync(item, AppSettings.ManualDownloadContent, AppSettings.ManualImageMode));
                UiHelper.Toast("Enregistré pour plus tard");
                return;
            }

            await Db.SetReadLaterAsync(item, !item.IsReadLater);
            UiHelper.Toast(item.IsReadLater ? "À lire plus tard" : "Retiré de « Plus tard »");
        }

        protected async Task SaveOfflineAsync(RssItem? item)
        {
            if (item == null || item.IsBusy) return;
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                await UiHelper.AlertAsync("Hors connexion", "Connectez-vous pour télécharger cet article.");
                return;
            }

            try
            {
                item.IsBusy = true;
                bool ok;
                if (!item.IsStored)
                {
                    var stored = await Task.Run(() => Rss.SaveLiveItemAsync(item, true, AppSettings.ManualImageMode));
                    ok = stored != null && stored.IsDownloaded;
                }
                else
                {
                    ok = await Task.Run(() => Rss.DownloadAndSaveItemContentAsync(item, AppSettings.ManualImageMode));
                }
                UiHelper.Toast(ok ? "Disponible hors-ligne" : "Téléchargement impossible (site protégé ?)");
                UpdateCountText();
            }
            catch (Exception ex)
            {
                CrashLog.Write("Liste.Telechargement", ex);
                await UiHelper.AlertAsync("Erreur", "Le serveur ne répond pas ou le lien est invalide.");
            }
            finally
            {
                item.IsBusy = false;
            }
        }

        protected async Task ShareAsync(RssItem? item)
        {
            if (item == null || string.IsNullOrEmpty(item.Link)) return;
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Uri = item.Link,
                Title = item.Title,
                Text = item.Title
            });
        }

        protected async Task ItemMenuAsync(RssItem? item)
        {
            if (item == null) return;

            var actions = new List<string>
            {
                item.IsRead ? "Marquer comme non lu" : "Marquer comme lu",
                item.IsFavorite ? "Retirer des favoris" : "Ajouter aux favoris",
                item.IsReadLater ? "Retirer de « Plus tard »" : "Lire plus tard",
                item.IsDownloaded ? "Retélécharger (images)" : "Enregistrer hors-ligne",
                "Archiver",
                "Ouvrir dans le navigateur",
                "Copier le lien",
                "Partager"
            };
            if (item.IsStored && item.IsDownloaded) actions.Add("Libérer l'espace (retirer le hors-ligne)");

            var choice = await UiHelper.ActionSheetAsync(HtmlHelper.Truncate(item.Title, 70), "Annuler", null, actions.ToArray());
            if (choice == null) return;

            switch (choice)
            {
                case "Marquer comme non lu":
                case "Marquer comme lu":
                    await ToggleReadAsync(item); break;
                case "Retirer des favoris":
                case "Ajouter aux favoris":
                    await ToggleFavoriteAsync(item); break;
                case "Retirer de « Plus tard »":
                case "Lire plus tard":
                    await ToggleReadLaterAsync(item); break;
                case "Enregistrer hors-ligne":
                    await SaveOfflineAsync(item); break;
                case "Retélécharger (images)":
                    item.ImagesCached = false;
                    item.IsDownloaded = false;
                    await SaveOfflineAsync(item); break;
                case "Archiver":
                    await ArchiveAsync(item); break;
                case "Ouvrir dans le navigateur":
                    await OpenExternalLink(item); break;
                case "Copier le lien":
                    await Clipboard.Default.SetTextAsync(item.Link);
                    UiHelper.Toast("Lien copié"); break;
                case "Partager":
                    await ShareAsync(item); break;
                case "Libérer l'espace (retirer le hors-ligne)":
                    await Db.ClearArticleContentAsync(item);
                    UiHelper.Toast("Contenu hors-ligne retiré"); break;
            }
        }

        protected async Task ArchiveAsync(RssItem item)
        {
            var full = item.IsStored ? await Db.GetItemByIdAsync(item.Id) ?? item : item;
            int r = await Db.InsertArchiveAsync(ArchiveItem.FromArticle(full));
            UiHelper.Toast(r > 0 ? "Ajouté aux archives" : "Déjà dans les archives");
        }

        public virtual void OnThemeChanged()
        {
            foreach (var i in Items) i.RefreshDisplay();
        }
    }
}
