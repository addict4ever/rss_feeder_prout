using Rss_feeder_prout.Helpers;
using Rss_feeder_prout.Models;
using Rss_feeder_prout.Services;
using Rss_feeder_prout.Views;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Media;
using Microsoft.Maui.Networking;

namespace Rss_feeder_prout.ViewModels
{
    /// <summary>
    /// Lecteur d'article.
    ///  - lit les articles ENREGISTRÉS (hors-ligne ou résumé) ET les articles EN LIGNE non enregistrés
    ///  - récupère l'article complet à la volée si besoin (sans l'enregistrer) puis propose de l'enregistrer
    ///  - réglages de lecture (taille, thème, police), synthèse vocale, précédent / suivant, favoris, archives…
    /// Paramètres : "Item" (RssItem), ou "itemId" (+ "IsArchive" = "true").
    /// </summary>
    [QueryProperty(nameof(ItemId), "itemId")]
    [QueryProperty(nameof(IsArchive), "IsArchive")]
    public class ArticleDetailViewModel : BaseViewModel, IQueryAttributable
    {
        private readonly SQLiteService _dbService;
        private readonly RssService _rssService;
        private readonly ReaderSession _session;

        private string? _extractedHtml;       // contenu complet récupéré à la volée (pas encore enregistré)
        private string _currentBody = string.Empty;
        private CancellationTokenSource? _loadCts;
        private CancellationTokenSource? _speechCts;
        private bool _loaded;

        public ArticleDetailViewModel(SQLiteService dbService, RssService rssService, ReaderSession session)
        {
            _dbService = dbService;
            _rssService = rssService;
            _session = session;

            ArchiveCommand = new Command(async () => await Archive());
            ShareCommand = new Command(async () => await Share());
            OpenExternalCommand = new Command(async () => await ExecuteOpenExternalCommand());
            OpenInAppWebCommand = new Command(async () => await OpenInAppWebAsync());
            DownloadContentCommand = new Command(async () => await ExecuteDownloadContentCommand());
            ShowSourceCommand = new Command(async () => await ExecuteShowSourceCommand());
            FavoriteCommand = new Command(async () => await ToggleFavoriteAsync());
            ReadLaterCommand = new Command(async () => await ToggleReadLaterAsync());
            ToggleReadCommand = new Command(async () => await ToggleReadAsync());
            FontSmallerCommand = new Command(async () => { AppSettings.ReaderFontSize -= 1; await RenderAsync(); });
            FontBiggerCommand = new Command(async () => { AppSettings.ReaderFontSize += 1; await RenderAsync(); });
            ReaderSettingsCommand = new Command(async () => await ReaderSettingsAsync());
            SpeakCommand = new Command(async () => await ToggleSpeechAsync());
            NextCommand = new Command(async () => await MoveAsync(_session.MoveNext()), () => HasNext);
            PreviousCommand = new Command(async () => await MoveAsync(_session.MovePrevious()), () => HasPrevious);
            NextUnreadCommand = new Command(async () => await MoveAsync(_session.MoveNextUnread()));
            CopyLinkCommand = new Command(async () => await CopyLinkAsync());
            MoreCommand = new Command(async () => await MoreAsync());
            ReloadFullCommand = new Command(async () => await LoadFullOnlineAsync(force: true));
        }

        // ------------------------------------------------------------------
        // Propriétés
        // ------------------------------------------------------------------

        private string _isArchive = string.Empty;
        public string IsArchive
        {
            get => _isArchive;
            set => SetProperty(ref _isArchive, value ?? string.Empty);
        }

        private string _itemId = string.Empty;
        public string ItemId
        {
            get => _itemId;
            set => SetProperty(ref _itemId, value ?? string.Empty);
        }

        private RssItem? _article;
        public RssItem? Article
        {
            get => _article;
            set
            {
                if (SetProperty(ref _article, value))
                    NotifyArticleState();
            }
        }

        public bool IsArchiveView => IsArchive == "true";

        private HtmlWebViewSource? _htmlSource;
        public HtmlWebViewSource? HtmlSource
        {
            get => _htmlSource;
            set => SetProperty(ref _htmlSource, value);
        }

        private string _contentHtml = string.Empty;
        /// <summary>HTML final affiché (conservé pour l'inspecteur de code source).</summary>
        public string ContentHtml
        {
            get => _contentHtml;
            set => SetProperty(ref _contentHtml, value);
        }

        private bool _isLoadingContent;
        public bool IsLoadingContent
        {
            get => _isLoadingContent;
            set => SetProperty(ref _isLoadingContent, value);
        }

        private string _loadingText = string.Empty;
        public string LoadingText
        {
            get => _loadingText;
            set => SetProperty(ref _loadingText, value);
        }

        private bool _isSpeaking;
        public bool IsSpeaking
        {
            get => _isSpeaking;
            set
            {
                if (SetProperty(ref _isSpeaking, value))
                    OnPropertyChanged(nameof(SpeakIcon));
            }
        }

        public string SpeakIcon => IsSpeaking ? "stop" : "speaker";

        public bool HasNext => !IsArchiveView && _session.HasNext;
        public bool HasPrevious => !IsArchiveView && _session.HasPrevious;
        public bool HasNavigation => !IsArchiveView && _session.Count > 1;
        public string PositionText => IsArchiveView ? string.Empty : _session.PositionText;

        public bool IsFavorite => Article?.IsFavorite == true;
        public bool IsReadLater => Article?.IsReadLater == true;
        public string FavoriteIconColorKey => IsFavorite ? "Warning" : "TextPrimary";

        /// <summary>Le bouton "Enregistrer" est utile si l'article n'est pas complet hors-ligne.</summary>
        public bool CanSave => Article != null && !IsArchiveView && (!Article.IsStored || !Article.IsDownloaded || !Article.ImagesCached);

        public string SaveText
        {
            get
            {
                if (Article == null) return "Enregistrer";
                if (!Article.IsStored) return "Enregistrer";
                if (!Article.IsDownloaded) return "Hors-ligne";
                return "Images";
            }
        }

        private Color _favoriteColor = Colors.Gray;
        public Color FavoriteColor
        {
            get => _favoriteColor;
            set => SetProperty(ref _favoriteColor, value);
        }

        private Color _readLaterColor = Colors.Gray;
        public Color ReadLaterColor
        {
            get => _readLaterColor;
            set => SetProperty(ref _readLaterColor, value);
        }

        private void NotifyArticleState()
        {
            Notify(nameof(IsFavorite), nameof(IsReadLater), nameof(CanSave), nameof(SaveText), nameof(FavoriteIconColorKey),
                   nameof(HasNext), nameof(HasPrevious), nameof(HasNavigation), nameof(PositionText), nameof(IsArchiveView));
            FavoriteColor = IsFavorite ? ThemeService.Palette.Warning : ThemeService.Palette.TextPrimary;
            ReadLaterColor = IsReadLater ? ThemeService.Palette.Info : ThemeService.Palette.TextPrimary;
            (NextCommand as Command)?.ChangeCanExecute();
            (PreviousCommand as Command)?.ChangeCanExecute();
        }

        // ------------------------------------------------------------------
        // Commandes
        // ------------------------------------------------------------------

        public ICommand OpenExternalCommand { get; }
        public ICommand OpenInAppWebCommand { get; }
        public ICommand DownloadContentCommand { get; }
        public ICommand ArchiveCommand { get; }
        public ICommand ShareCommand { get; }
        public ICommand ShowSourceCommand { get; }
        public ICommand FavoriteCommand { get; }
        public ICommand ReadLaterCommand { get; }
        public ICommand ToggleReadCommand { get; }
        public ICommand FontSmallerCommand { get; }
        public ICommand FontBiggerCommand { get; }
        public ICommand ReaderSettingsCommand { get; }
        public ICommand SpeakCommand { get; }
        public ICommand NextCommand { get; }
        public ICommand PreviousCommand { get; }
        public ICommand NextUnreadCommand { get; }
        public ICommand CopyLinkCommand { get; }
        public ICommand MoreCommand { get; }
        public ICommand ReloadFullCommand { get; }

        // ------------------------------------------------------------------
        // Paramètres de navigation
        // ------------------------------------------------------------------

        public void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            if (_loaded) return;

            if (query.TryGetValue("IsArchive", out object? isArchiveValue))
                IsArchive = isArchiveValue?.ToString() ?? string.Empty;

            if (query.TryGetValue("Item", out object? itemObj) && itemObj is RssItem item)
            {
                _loaded = true;
                _ = LoadItemAsync(item);
                return;
            }

            if (query.TryGetValue("itemId", out object? itemIdValue))
            {
                ItemId = itemIdValue?.ToString() ?? string.Empty;
                _loaded = true;
                _ = LoadArticleById();
            }
        }

        // ------------------------------------------------------------------
        // Chargement
        // ------------------------------------------------------------------

        /// <summary>Conservé : chargement par Id (archives ou articles enregistrés).</summary>
        private async Task LoadArticleById()
        {
            if (!int.TryParse(ItemId, out int id)) return;

            try
            {
                IsBusy = true;
                RssItem? item = null;

                if (IsArchiveView)
                {
                    var archived = await _dbService.GetArchiveByIdAsync(id);
                    item = archived?.ToRssItem();
                }
                else
                {
                    item = await _dbService.GetItemByIdAsync(id);
                }

                if (item != null) await LoadItemAsync(item);
                else await ShowMessageAsync("Article introuvable", "Il a peut-être été supprimé par le nettoyage automatique.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DETAIL ERROR]: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task LoadItemAsync(RssItem item)
        {
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            _extractedHtml = null;
            StopSpeech();

            try
            {
                // Article enregistré : on recharge la version complète (les listes n'ont pas le contenu)
                if (item.IsStored && !IsArchiveView && string.IsNullOrEmpty(item.ContentHtml))
                {
                    var full = await _dbService.GetItemByIdAsync(item.Id);
                    if (full != null)
                    {
                        item.ContentHtml = full.ContentHtml;
                        item.IsDownloaded = full.IsDownloaded;
                        item.ImagesCached = full.ImagesCached;
                        item.IsFavorite = full.IsFavorite;
                        item.IsReadLater = full.IsReadLater;
                        if (!string.IsNullOrEmpty(full.LocalImagePath)) item.LocalImagePath = full.LocalImagePath;
                    }
                }

                Article = item;
                Title = item.Title;

                // Marquer comme lu
                if (!IsArchiveView && !item.IsRead && AppSettings.MarkReadOnOpen)
                {
                    if (item.IsStored) await _dbService.MarkItemAsReadAsync(item);
                    else item.IsRead = true;
                }

                await LoadArticleContentAsync(item);
            }
            catch (Exception ex)
            {
                CrashLog.Write("Lecteur.Load", ex);
                await ShowMessageAsync("Erreur", "Impossible d'afficher cet article.");
            }
        }

        /// <summary>
        /// Conservé (réécrit) : prépare le contenu à afficher.
        ///  1. contenu téléchargé -> affichage immédiat (hors-ligne)
        ///  2. sinon contenu du flux / résumé immédiatement, puis récupération de l'article complet en ligne
        /// </summary>
        private async Task LoadArticleContentAsync(RssItem item)
        {
            if (item.IsDownloaded && !string.IsNullOrWhiteSpace(item.ContentHtml))
            {
                _currentBody = await Task.Run(() => CleanArticleHtmlAsync(item.ContentHtml, item.SiteId));
                await RenderAsync();
                return;
            }

            // Contenu du flux ou résumé
            var feedBody = !string.IsNullOrWhiteSpace(item.ContentHtml)
                ? item.ContentHtml
                : $"<p>{HtmlHelper.Encode(item.Summary)}</p>";
            _currentBody = feedBody;

            bool online = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
            string notice;
            if (!online)
                notice = "Vous êtes hors connexion : seul le résumé est disponible. Enregistrez les articles hors-ligne lors de la synchronisation pour les lire partout.";
            else if (AppSettings.AutoExtractOnOpen)
                notice = "Chargement de l'article complet…";
            else
                notice = "Résumé seulement. Touchez « Article complet » pour le récupérer.";

            await RenderAsync(notice);

            if (online && AppSettings.AutoExtractOnOpen)
                await LoadFullOnlineAsync(force: false);
        }

        /// <summary>Récupère l'article complet depuis le site, SANS l'enregistrer.</summary>
        private async Task LoadFullOnlineAsync(bool force)
        {
            var item = Article;
            if (item == null || string.IsNullOrWhiteSpace(item.Link)) return;
            if (!force && item.IsDownloaded) return;

            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                UiHelper.Toast("Hors connexion");
                return;
            }

            var ct = _loadCts?.Token ?? CancellationToken.None;
            try
            {
                IsLoadingContent = true;
                LoadingText = "Récupération de l'article complet…";

                var site = item.SiteId.HasValue ? await _dbService.GetSiteByIdAsync(item.SiteId.Value) : null;

                // CORRIGÉ : l'extraction (téléchargement + AngleSharp + SmartReader + contournements) tournait
                // sur le thread de l'interface -> l'app se figeait puis Android la fermait. Tout se fait en arrière-plan.
                var currentBody = _currentBody;
                var extraction = await Task.Run(async () =>
                {
                    var html = await _rssService.ExtractArticleHtmlAsync(item.Link, site, ct);
                    bool better = !string.IsNullOrWhiteSpace(html) && HtmlHelper.TextLength(html) > HtmlHelper.TextLength(currentBody);
                    string cleaned = better ? await CleanArticleHtmlAsync(html!, item.SiteId) : string.Empty;
                    int minutes = better ? HtmlHelper.EstimateReadingMinutes(html) : 0;
                    bool tooShort = !better && HtmlHelper.TextLength(currentBody) < 300;
                    return (html, better, cleaned, minutes, tooShort);
                }, ct);
                if (ct.IsCancellationRequested || !ReferenceEquals(item, Article)) return;

                if (extraction.better)
                {
                    _extractedHtml = extraction.html;
                    _currentBody = extraction.cleaned;
                    item.ReadingMinutes = extraction.minutes;
                    await RenderAsync(item.IsStored
                        ? "Article complet affiché (en ligne). Touchez « Hors-ligne » pour le garder."
                        : "Article complet affiché (en ligne). Touchez « Enregistrer » pour le garder.");
                }
                else if (extraction.tooShort)
                {
                    await RenderAsync("Impossible de récupérer l'article complet (site protégé ou contenu dynamique). Ouvrez-le dans le navigateur.");
                }
                else
                {
                    await RenderAsync();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                CrashLog.Write("Lecteur.Extraction", ex);
            }
            finally
            {
                IsLoadingContent = false;
            }
        }

        private async Task RenderAsync(string? notice = null)
        {
            var item = Article;
            if (item == null) return;

            try
            {
                var body = _currentBody;
                var watched = HtmlHelper.ParseKeywords(AppSettings.HighlightKeywords);
                // Construction de la page hors du thread UI (regex sur de gros textes)
                var html = await Task.Run(() => ReaderHtmlBuilder.BuildAsync(item, body, notice, watched));

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (!ReferenceEquals(item, Article)) return;
                    ContentHtml = html;
                    HtmlSource = new HtmlWebViewSource
                    {
                        Html = html,
                        // Dossier des images : les images hors-ligne (file://) se chargent directement
                        BaseUrl = ReaderHtmlBuilder.ReaderBaseUrl
                    };
                    Title = item.Title;
                    NotifyArticleState();
                });
            }
            catch (Exception ex)
            {
                CrashLog.Write("Lecteur.Render", ex);
                await ShowMessageAsync("Affichage impossible", "Cet article n'a pas pu être affiché. Essayez « Ouvrir dans le navigateur ».");
            }
        }

        private async Task ShowMessageAsync(string title, string text)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                HtmlSource = new HtmlWebViewSource { Html = ReaderHtmlBuilder.Message(title, text) };
            });
        }

        /// <summary>
        /// Conservé : nettoyage des structures fixes (ZATAZ et autres) + sélecteurs du site.
        /// CORRIGÉ : les sélecteurs sont appliqués avec AngleSharp (les regex cassaient les balises imbriquées).
        /// </summary>
        private async Task<string> CleanArticleHtmlAsync(string html, int? siteId)
        {
            string processedHtml = html;
            try
            {
                // A. Coupe radicale : On garde uniquement ce qu'il y a dans l'article
                if (Regex.Matches(processedHtml, "</article>", RegexOptions.IgnoreCase).Count == 1)
                {
                    processedHtml = Regex.Replace(processedHtml, @"</article>.*", "</article>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                }

                // B-E. Partage, newsletter, footers, chargement infini
                var classes = new[]
                {
                    "social-share", "share-links", "ss-container", "entry-share",
                    "newsletter", "subscribe", "mailchimp", "wp-block-buttons",
                    "single-footer", "sfoter-sec", "rb-s-container", "single-related", "block-wrap", "post-navigation", "author-box"
                };
                var selectors = string.Join(",", classes.Select(c => "[class*='" + c + "']")) + ",#single-infinite-point";

                var site = siteId.HasValue ? await _dbService.GetSiteByIdAsync(siteId.Value) : null;
                if (site != null && !string.IsNullOrWhiteSpace(site.CustomCleanSelectors))
                    selectors += "," + site.CustomCleanSelectors;

                using var context = AngleSharp.BrowsingContext.New(AngleSharp.Configuration.Default);
                using var doc = await AngleSharp.BrowsingContextExtensions.OpenAsync(context, req => req.Content(processedHtml));
                foreach (var sel in selectors.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    try
                    {
                        foreach (var el in doc.QuerySelectorAll(sel.Trim()).ToList()) el.Remove();
                    }
                    catch { /* sélecteur invalide */ }
                }
                processedHtml = doc.Body?.InnerHtml ?? processedHtml;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CLEAN ERROR] : {ex.Message}");
            }
            return processedHtml;
        }

        // ------------------------------------------------------------------
        // Actions
        // ------------------------------------------------------------------

        private async Task MoveAsync(RssItem? next)
        {
            if (next == null) return;
            await LoadItemAsync(next);
        }

        /// <summary>Conservé : enregistrer hors-ligne (texte + images selon les réglages).</summary>
        private async Task ExecuteDownloadContentCommand()
        {
            var item = Article;
            if (item == null || IsArchiveView) return;

            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet && _extractedHtml == null && !item.IsDownloaded)
            {
                await UiHelper.AlertAsync("Hors connexion", "Connectez-vous pour télécharger cet article.");
                return;
            }

            try
            {
                IsLoadingContent = true;
                LoadingText = AppSettings.ManualImageMode == ImageMode.None ? "Enregistrement…" : "Enregistrement du texte et des images…";
                var mode = AppSettings.ManualImageMode;

                if (!item.IsStored)
                {
                    var extracted = _extractedHtml;
                    var stored = await Task.Run(() => _rssService.SaveLiveItemAsync(item, downloadFull: extracted == null, mode, extracted));
                    if (stored != null)
                    {
                        item.Id = stored.Id;
                        item.IsDownloaded = stored.IsDownloaded;
                        item.ImagesCached = stored.ImagesCached;
                        item.ContentHtml = stored.ContentHtml;
                    }
                }
                else if (_extractedHtml != null && !item.IsDownloaded)
                {
                    var extracted = _extractedHtml;
                    await Task.Run(() => _rssService.SaveExtractedContentAsync(item, extracted, mode));
                }
                else
                {
                    // Déjà téléchargé : on complète les images
                    if (item.IsDownloaded && !item.ImagesCached && mode == ImageMode.None) mode = ImageMode.Optimized;
                    await Task.Run(() => _rssService.DownloadAndSaveItemContentAsync(item, mode));
                }

                // Recharge depuis la base pour afficher la version hors-ligne
                if (item.IsStored)
                {
                    var full = await _dbService.GetItemByIdAsync(item.Id);
                    if (full != null)
                    {
                        item.ContentHtml = full.ContentHtml;
                        item.IsDownloaded = full.IsDownloaded;
                        item.ImagesCached = full.ImagesCached;
                        if (!string.IsNullOrEmpty(full.LocalImagePath)) item.LocalImagePath = full.LocalImagePath;
                    }
                }

                if (item.IsDownloaded)
                {
                    _currentBody = await Task.Run(() => CleanArticleHtmlAsync(item.ContentHtml, item.SiteId));
                    await RenderAsync();
                    UiHelper.Toast(item.ImagesCached ? "Disponible hors-ligne (avec images)" : "Disponible hors-ligne");
                }
                else
                {
                    UiHelper.Toast("Enregistré (résumé seulement)");
                    NotifyArticleState();
                }
            }
            catch (Exception ex)
            {
                CrashLog.Write("Lecteur.Telechargement", ex);
                await UiHelper.AlertAsync("Erreur", "Échec du téléchargement du contenu pour la lecture hors ligne.");
            }
            finally
            {
                IsLoadingContent = false;
            }
        }

        /// <summary>Conservé : archivage permanent.</summary>
        private async Task Archive()
        {
            var item = Article;
            if (item == null || IsArchiveView) return;

            try
            {
                var archive = ArchiveItem.FromArticle(item);
                if (string.IsNullOrWhiteSpace(archive.ContentHtml) && _extractedHtml != null)
                    archive.ContentHtml = _extractedHtml;

                int result = await _dbService.InsertArchiveAsync(archive);
                UiHelper.Toast(result > 0 ? "Article ajouté à vos archives" : "Cet article est déjà dans vos archives");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ARCHIVE ERROR]: {ex.Message}");
                await UiHelper.AlertAsync("Erreur", "L'archivage a échoué.");
            }
        }

        /// <summary>Conservé : partage du lien.</summary>
        private async Task Share()
        {
            if (Article == null || string.IsNullOrEmpty(Article.Link)) return;

            await Microsoft.Maui.ApplicationModel.DataTransfer.Share.Default.RequestAsync(new ShareTextRequest
            {
                Uri = Article.Link,
                Title = Article.Title,
                Text = $"Regarde cet article : {Article.Title}"
            });
        }

        private async Task ToggleFavoriteAsync()
        {
            var item = Article;
            if (item == null || IsArchiveView) return;

            if (!item.IsStored)
            {
                item.IsFavorite = true;
                await _rssService.SaveLiveItemAsync(item, false, AppSettings.ManualImageMode, _extractedHtml);
                UiHelper.Toast("Enregistré et ajouté aux favoris");
            }
            else
            {
                await _dbService.SetFavoriteAsync(item, !item.IsFavorite);
                UiHelper.Toast(item.IsFavorite ? "Ajouté aux favoris" : "Retiré des favoris");
            }
            NotifyArticleState();
        }

        private async Task ToggleReadLaterAsync()
        {
            var item = Article;
            if (item == null || IsArchiveView) return;

            if (!item.IsStored)
            {
                item.IsReadLater = true;
                await _rssService.SaveLiveItemAsync(item, false, AppSettings.ManualImageMode, _extractedHtml);
                UiHelper.Toast("Enregistré pour plus tard");
            }
            else
            {
                await _dbService.SetReadLaterAsync(item, !item.IsReadLater);
                UiHelper.Toast(item.IsReadLater ? "À lire plus tard" : "Retiré de « Plus tard »");
            }
            NotifyArticleState();
        }

        private async Task ToggleReadAsync()
        {
            var item = Article;
            if (item == null || IsArchiveView) return;
            await _dbService.SetReadAsync(item, !item.IsRead);
            UiHelper.Toast(item.IsRead ? "Marqué comme lu" : "Marqué comme non lu");
        }

        /// <summary>Conservé : ouverture dans le navigateur.</summary>
        private async Task ExecuteOpenExternalCommand(bool silent = false)
        {
            if (Article == null || string.IsNullOrWhiteSpace(Article.Link)) return;

            try
            {
                await UiHelper.OpenBrowserAsync(Article.Link);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DETAIL] Failed to open external link: {ex.Message}");
                if (!silent)
                    await UiHelper.AlertAsync("Erreur", "Impossible d'ouvrir le lien web de l'article.");
            }
        }

        private async Task OpenInAppWebAsync()
        {
            if (Article == null || string.IsNullOrWhiteSpace(Article.Link)) return;
            await UiHelper.GoToAsync(nameof(ItemDetailPage), new Dictionary<string, object>
            {
                ["url"] = Article.Link,
                ["title"] = Article.Title
            });
        }

        /// <summary>Conservé : inspecteur de code source HTML.</summary>
        private async Task ExecuteShowSourceCommand()
        {
            if (string.IsNullOrEmpty(ContentHtml))
            {
                await UiHelper.AlertAsync("Information", "Le code source n'est pas encore disponible.");
                return;
            }

            try
            {
                await UiHelper.GoToAsync(nameof(SourceCodePage), new Dictionary<string, object>
                {
                    { "HtmlContent", _extractedHtml ?? Article?.ContentHtml ?? ContentHtml }
                });
            }
            catch (Exception ex)
            {
                await UiHelper.AlertAsync("Erreur de navigation", $"Impossible d'ouvrir l'inspecteur : {ex.Message}");
            }
        }

        private async Task CopyLinkAsync()
        {
            if (Article == null || string.IsNullOrWhiteSpace(Article.Link)) return;
            await Clipboard.Default.SetTextAsync(Article.Link);
            UiHelper.Toast("Lien copié");
        }

        private async Task ReaderSettingsAsync()
        {
            var themeLabel = AppSettings.ReaderTheme switch
            {
                "dark" => "Sombre", "light" => "Clair", "sepia" => "Sépia", "black" => "Noir (OLED)", _ => "Comme l'application"
            };
            var choice = await UiHelper.ActionSheetAsync("Affichage du lecteur", "Fermer", null,
                $"Thème : {themeLabel}",
                AppSettings.ReaderSerif ? "Police : avec empattement → sans" : "Police : sans empattement → avec",
                $"Interligne : {AppSettings.ReaderLineHeight:0.0}",
                AppSettings.ReaderJustify ? "Texte justifié → aligné à gauche" : "Texte aligné à gauche → justifié",
                "Taille du texte +", "Taille du texte −");
            if (choice == null) return;

            if (choice.StartsWith("Thème"))
            {
                var t = await UiHelper.ActionSheetAsync("Thème du lecteur", "Annuler", null, "Comme l'application", "Sombre", "Clair", "Sépia", "Noir (OLED)");
                if (t == null) return;
                AppSettings.ReaderTheme = t switch { "Sombre" => "dark", "Clair" => "light", "Sépia" => "sepia", "Noir (OLED)" => "black", _ => "app" };
            }
            else if (choice.StartsWith("Police")) AppSettings.ReaderSerif = !AppSettings.ReaderSerif;
            else if (choice.StartsWith("Interligne"))
            {
                var next = AppSettings.ReaderLineHeight + 0.15;
                AppSettings.ReaderLineHeight = next > 2.05 ? 1.35 : next;
            }
            else if (choice.StartsWith("Texte")) AppSettings.ReaderJustify = !AppSettings.ReaderJustify;
            else if (choice == "Taille du texte +") AppSettings.ReaderFontSize += 1;
            else if (choice == "Taille du texte −") AppSettings.ReaderFontSize -= 1;

            await RenderAsync();
        }

        private async Task MoreAsync()
        {
            if (Article == null) return;
            var actions = new List<string>();
            if (!IsArchiveView)
            {
                actions.Add(Article.IsRead ? "Marquer comme non lu" : "Marquer comme lu");
                actions.Add(IsReadLater ? "Retirer de « Plus tard »" : "Lire plus tard");
                actions.Add("Archiver");
                actions.Add("Article complet (recharger en ligne)");
            }
            actions.Add("Ouvrir la page web dans l'app");
            actions.Add("Ouvrir dans le navigateur");
            actions.Add("Copier le lien");
            actions.Add("Affichage du lecteur…");
            actions.Add("Voir le code source");
            if (!IsArchiveView && HasNavigation) actions.Add("Article non lu suivant");

            var choice = await UiHelper.ActionSheetAsync(HtmlHelper.Truncate(Article.Title, 60), "Annuler", null, actions.ToArray());
            switch (choice)
            {
                case "Marquer comme non lu":
                case "Marquer comme lu": await ToggleReadAsync(); break;
                case "Retirer de « Plus tard »":
                case "Lire plus tard": await ToggleReadLaterAsync(); break;
                case "Archiver": await Archive(); break;
                case "Article complet (recharger en ligne)": await LoadFullOnlineAsync(true); break;
                case "Ouvrir la page web dans l'app": await OpenInAppWebAsync(); break;
                case "Ouvrir dans le navigateur": await ExecuteOpenExternalCommand(); break;
                case "Copier le lien": await CopyLinkAsync(); break;
                case "Affichage du lecteur…": await ReaderSettingsAsync(); break;
                case "Voir le code source": await ExecuteShowSourceCommand(); break;
                case "Article non lu suivant": await MoveAsync(_session.MoveNextUnread()); break;
            }
        }

        // ------------------------------------------------------------------
        // Synthèse vocale
        // ------------------------------------------------------------------

        private async Task ToggleSpeechAsync()
        {
            if (IsSpeaking)
            {
                StopSpeech();
                return;
            }

            var item = Article;
            if (item == null) return;

            var text = item.Title + ".\n" + HtmlHelper.StripHtml(_currentBody);
            if (string.IsNullOrWhiteSpace(text)) return;

            _speechCts = new CancellationTokenSource();
            var ct = _speechCts.Token;
            IsSpeaking = true;

            try
            {
                SpeechOptions? options = null;
                try
                {
                    var locales = await TextToSpeech.Default.GetLocalesAsync();
                    var fr = locales.FirstOrDefault(l => l.Language.StartsWith("fr", StringComparison.OrdinalIgnoreCase));
                    if (fr != null) options = new SpeechOptions { Locale = fr };
                }
                catch { }

                // Découpage par paragraphes (Android limite la longueur d'un énoncé)
                foreach (var chunk in SplitForSpeech(text, 3000))
                {
                    if (ct.IsCancellationRequested) break;
                    await TextToSpeech.Default.SpeakAsync(chunk, options, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TTS] {ex.Message}");
                UiHelper.Toast("Synthèse vocale indisponible");
            }
            finally
            {
                IsSpeaking = false;
            }
        }

        private static IEnumerable<string> SplitForSpeech(string text, int max)
        {
            var paragraphs = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var current = new System.Text.StringBuilder();
            foreach (var p in paragraphs)
            {
                if (current.Length + p.Length + 1 > max && current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
                if (p.Length > max)
                {
                    for (int i = 0; i < p.Length; i += max)
                        yield return p.Substring(i, Math.Min(max, p.Length - i));
                    continue;
                }
                current.Append(p).Append('\n');
            }
            if (current.Length > 0) yield return current.ToString();
        }

        public void StopSpeech()
        {
            try { _speechCts?.Cancel(); } catch { }
            IsSpeaking = false;
        }

        /// <summary>Appelé quand la page disparaît.</summary>
        public void OnDisappearing()
        {
            StopSpeech();
        }
    }
}
