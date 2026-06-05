using Rss_feeder_prout.Models;
using Rss_feeder_prout.Services;
using Rss_feeder_prout.Views;
using Microsoft.Maui.Controls;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;
using System;
using Microsoft.Maui.ApplicationModel;
using System.Collections.Generic;
using Microsoft.Maui.Networking; // 🎯 Ajout pour vérifier la connexion Internet
using AngleSharp;
using AngleSharp.Html.Dom;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using System.Text.RegularExpressions;

namespace Rss_feeder_prout.ViewModels
{
    // 🎯 Important : L'interface IQueryAttributable permet de recevoir des paramètres d'URL
    [QueryProperty(nameof(ItemId), "itemId")]
    [QueryProperty(nameof(IsArchive), "IsArchive")]
    public class ArticleDetailViewModel : BaseViewModel, IQueryAttributable
    {
        private readonly SQLiteService _dbService;
        private readonly RssService _rssService;

        // --- Propriétés de Modèle ---
        private string _isArchive;
        public string IsArchive
        {
            get => _isArchive;
            set => SetProperty(ref _isArchive, value);
        }

        private RssItem _article;
        public RssItem Article
        {
            get => _article;
            set => SetProperty(ref _article, value);
        }

        private string _pageTitle;
        public string Title
        {
            get => _pageTitle;
            set => SetProperty(ref _pageTitle, value);
        }

        private string _itemId;
        /// <summary>
        /// Reçoit l'ID de l'article de l'URL de navigation (Shell)
        /// </summary>
        public string ItemId
        {
            get => _itemId;
            set
            {
                SetProperty(ref _itemId, value);
                // Le chargement est géré par ApplyQueryAttributes, mais on le garde ici en cas d'assignation directe
                // Task.Run(LoadArticleById); 
            }
        }

        // --- Propriétés d'Affichage ---

        private string _contentHtml;
        /// <summary>
        /// Contenu HTML prêt à être affiché dans un WebView
        /// </summary>
        public string ContentHtml
        {
            get => _contentHtml;
            set => SetProperty(ref _contentHtml, value);
        }

        public ICommand OpenExternalCommand { get; }
        public ICommand DownloadContentCommand { get; }

        public ICommand ArchiveCommand { get; }
        public ICommand ShareCommand { get; }

        public ICommand ShowSourceCommand { get; }

        // ----------------------------------------------------------------------

        public ArticleDetailViewModel(SQLiteService dbService, RssService rssService)
        {
            _dbService = dbService;
            _rssService = rssService;

            ArchiveCommand = new Command(async () => await Archive());
            ShareCommand = new Command(async () => await Share());

            OpenExternalCommand = new Command(async () => await ExecuteOpenExternalCommand());
            DownloadContentCommand = new Command(async () => await ExecuteDownloadContentCommand(),
                                               () => Article != null && !Article.IsDownloaded && !IsBusy);

            ShowSourceCommand = new Command(async () => await ExecuteShowSourceCommand());
        }

        /// <summary>
        /// Méthode requise par IQueryAttributable pour récupérer les paramètres.
        /// </summary>
        /// 
        private async Task ExecuteShowSourceCommand()
        {
            // 1. Vérification si le contenu existe
            if (string.IsNullOrEmpty(ContentHtml))
            {
                await Shell.Current.DisplayAlert("Information", "Le code source n'est pas encore disponible. Attendez la fin du téléchargement.", "OK");
                return;
            }

            try
            {
                var navigationParameter = new Dictionary<string, object>
        {
            { "HtmlContent", ContentHtml }
        };

                await Shell.Current.GoToAsync(nameof(SourceCodePage), navigationParameter);
            }
            catch (Exception ex)
            {
                // 2. Affichage de l'erreur précise en cas de problème de navigation
                Debug.WriteLine($"[ERROR] XML Navigation: {ex.Message}");
                await Shell.Current.DisplayAlert("Erreur de navigation", $"Impossible d'ouvrir l'inspecteur : {ex.Message}", "OK");
            }
        }

        public void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            // Récupération de l'ID
            if (query.TryGetValue("itemId", out object itemIdValue))
            {
                ItemId = itemIdValue.ToString();
            }

            // Récupération du flag IsArchive
            if (query.TryGetValue("IsArchive", out object isArchiveValue))
            {
                IsArchive = isArchiveValue.ToString();
            }

            // On lance le chargement une fois qu'on a tous les paramètres
            if (!string.IsNullOrEmpty(ItemId))
            {
                Task.Run(LoadArticleById);
            }
        }

        private async Task Archive()
        {
            if (Article == null) return;

            try
            {
                IsBusy = true;

                // 1. On crée l'objet Archive en mappant les propriétés du RssItem actuel
                var archive = new ArchiveItem
                {
                    Title = Article.Title,
                    Summary = Article.Summary,
                    PublishDate = Article.PublishDate,
                    ArticleGuid = Article.ArticleGuid,
                    Link = Article.Link,
                    Author = Article.Author,
                    ImageUrl = Article.ImageUrl,
                    ContentHtml = Article.ContentHtml,
                    SiteName = Article.SiteName,
                    ArchivedAt = DateTime.Now
                };

                // 2. On l'insère via le service
                int result = await _dbService.InsertArchiveAsync(archive);

                if (result > 0)
                {
                    await Shell.Current.DisplayAlert("Succès", "Article ajouté à vos archives !", "OK");
                }
                else
                {
                    // Cas où le GUID existe déjà dans la table Archives
                    await Shell.Current.DisplayAlert("Info", "Cet article est déjà présent dans vos archives.", "OK");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ARCHIVE ERROR]: {ex.Message}");
                await Shell.Current.DisplayAlert("Erreur", "L'archivage a échoué. Vérifiez votre connexion à la base de données.", "OK");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task Share()
        {
            if (Article == null || string.IsNullOrEmpty(Article.Link))
                return;

            // On utilise la classe Share intégrée nativement à Microsoft.Maui.ApplicationModel
            // Ce n'est pas le "Toolkit", c'est le coeur de MAUI.
            await Microsoft.Maui.ApplicationModel.DataTransfer.Share.Default.RequestAsync(new ShareTextRequest
            {
                Uri = Article.Link,
                Title = Article.Title,
                Text = $"Regarde cet article : {Article.Title}"
            });
        }

        private async Task LoadArticleById()
        {
            if (string.IsNullOrWhiteSpace(ItemId) || IsBusy) return;

            if (!int.TryParse(ItemId, out int id)) return;

            try
            {
                IsBusy = true;
                RssItem item = null;

                // 🎯 C'EST ICI QUE ÇA SE PASSE :
                if (IsArchive == "true")
                {
                    // On va chercher dans la table Archives
                    var archived = await _dbService.GetArchiveByIdAsync(id);
                    if (archived != null)
                    {
                        // On transforme l'ArchiveItem en RssItem pour que le XAML l'affiche
                        item = new RssItem
                        {
                            Id = archived.Id,
                            Title = archived.Title,
                            ContentHtml = archived.ContentHtml,
                            Summary = archived.Summary,
                            ImageUrl = archived.ImageUrl,
                            SiteName = archived.SiteName,
                            Link = archived.Link,
                            PublishDate = archived.PublishDate,
                            IsDownloaded = true
                        };
                    }
                }
                else
                {
                    // Chargement normal (Page d'accueil)
                    item = await _dbService.GetItemByIdAsync(id);
                    if (item != null && !item.IsRead)
                    {
                        item.IsRead = true;
                        await _dbService.MarkItemAsReadAsync(item);
                    }
                }

                if (item != null)
                {
                    Article = item;
                    Title = item.Title;

                    // On s'assure que le HTML est bien envoyé à la propriété ContentHtml
                    ContentHtml = item.ContentHtml;

                    // Force le rafraîchissement du contenu
                    await LoadArticleContentAsync(item);
                }
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

        private async Task LoadArticleContentAsync(RssItem item)
        {
            if (item == null) return;

            string processedHtml = item.ContentHtml;

            if (item.IsDownloaded && !string.IsNullOrWhiteSpace(item.ContentHtml))
            {
                try
                {
                    // --- 1. NETTOYAGE DES STRUCTURES FIXES (ZATAZ ET AUTRES) ---

                    // A. Coupe radicale : On garde uniquement ce qu'il y a dans l'article
                    if (processedHtml.Contains("</article>", StringComparison.OrdinalIgnoreCase))
                    {
                        processedHtml = Regex.Replace(processedHtml, @"</article>.*", "</article>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    }

                    // B. Suppression des éléments de partage (Zataz utilise souvent 'social-share' ou 'share-links')
                    string socialPattern = @"<div[^>]+class=['""][^""]*(social-share|share-links|ss-container|entry-share)[^""]*['""][^>]*>.*?</div>";
                    processedHtml = Regex.Replace(processedHtml, socialPattern, string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);

                    // C. Suppression des encadrés "Abonnez-vous" ou "Newsletter"
                    string newsletterPattern = @"<div[^>]+class=['""][^""]*(newsletter|subscribe|mailchimp|wp-block-buttons)[^""]*['""][^>]*>.*?</div>";
                    processedHtml = Regex.Replace(processedHtml, newsletterPattern, string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);

                    // D. Nettoyage des footers et blocs de recommandation déjà identifiés
                    string footerPattern = @"<div[^>]+class=['""][^""]*(single-footer|sfoter-sec|rb-s-container|single-related|block-wrap|post-navigation|author-box)[^""]*['""][^>]*>.*?</div>";
                    processedHtml = Regex.Replace(processedHtml, footerPattern, string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);

                    // E. Supprime le point de chargement infini
                    processedHtml = Regex.Replace(processedHtml, @"<div[^>]+id=['""]single-infinite-point['""][^>]*>.*?</div>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);


                    // --- 2. NETTOYAGE DYNAMIQUE PAR SÉLECTEURS DU SITE ---
                    var site = await _dbService.GetSiteByIdAsync(item.SiteId ?? 0);

                    if (site != null && !string.IsNullOrWhiteSpace(site.CustomCleanSelectors))
                    {
                        var selectors = site.CustomCleanSelectors.Split(',', StringSplitOptions.RemoveEmptyEntries);

                        foreach (var selector in selectors)
                        {
                            string cleanSelector = selector.Trim();

                            // Regex pour ID (#id)
                            if (cleanSelector.StartsWith("#"))
                            {
                                string idName = cleanSelector.Substring(1);
                                string pattern = $@"<([a-zA-Z0-9]+)[^>]+id=['""]{idName}['""][^>]*>.*?</\1>";
                                processedHtml = Regex.Replace(processedHtml, pattern, string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
                            }
                            // Regex pour Classe (.classe)
                            else if (cleanSelector.StartsWith("."))
                            {
                                string className = cleanSelector.Substring(1);
                                string pattern = $@"<([a-zA-Z0-9]+)[^>]+class=['""][^""]*{className}[^""]*['""][^>]*>.*?</\1>";
                                processedHtml = Regex.Replace(processedHtml, pattern, string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CLEAN ERROR] : {ex.Message}");
                }
            }
            else if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
            {
                await ExecuteOpenExternalCommand(silent: true);
                processedHtml = "<html><body style='color:white;text-align:center;'><h3>Redirection...</h3></body></html>";
            }
            else
            {
                processedHtml = $"<html><body style='color:white;'><h3>Mode Hors-ligne</h3><p>{item.Summary}</p></body></html>";
            }

            // 🎯 MISE À JOUR DE L'INTERFACE (CSS optimisé pour la lecture)
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ContentHtml = $@"<html>
            <head>
                <style>
                    body {{ color: white; background-color: #121212; font-family: -apple-system, sans-serif; line-height: 1.6; padding: 15px; font-size: 17px; }} 
                    img {{ max-width: 100%; height: auto; border-radius: 8px; margin: 10px 0; }} 
                    a {{ color: #007AFF; text-decoration: none; }}
                    h1, h2, h3 {{ color: #f0f0f0; }}
                    iframe {{ width: 100%; border: none; }}
                </style>
            </head>
            <body>{processedHtml}</body>
        </html>";
                Title = item.Title;
            });
        }

        // --- Commandes ---

        private async Task ExecuteDownloadContentCommand()
        {
            if (Article == null || Article.IsDownloaded) return;

            try
            {
                IsBusy = true;
                Article.IsBusy = true;

                await _rssService.DownloadAndSaveItemContentAsync(Article);

                // Rafraîchir l'affichage du contenu HTML avec le contenu téléchargé
                await LoadArticleContentAsync(Article);

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DETAIL] Download failed: {ex.Message}");
                await Shell.Current.DisplayAlert("Erreur", "Échec du téléchargement du contenu pour la lecture hors ligne.", "OK");
            }
            finally
            {
                IsBusy = false;
                Article.IsBusy = false;
                ((Command)DownloadContentCommand).ChangeCanExecute();
            }
        }

        // 🎯 MODIFICATION : Ajout d'un paramètre optionnel pour ne pas afficher l'alerte
        private async Task ExecuteOpenExternalCommand(bool silent = false)
        {
            if (Article == null || string.IsNullOrWhiteSpace(Article.Link)) return;

            try
            {
                await Launcher.OpenAsync(new Uri(Article.Link));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DETAIL] Failed to open external link: {ex.Message}");
                if (!silent)
                {
                    await Shell.Current.DisplayAlert("Erreur", "Impossible d'ouvrir le lien web de l'article.", "OK");
                }
            }
        }
    }
}