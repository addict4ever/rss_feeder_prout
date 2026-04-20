using Rss_feeder_prout.Models;
using Rss_feeder_prout.Services;
using Microsoft.Maui.Controls;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;
using System;
using Microsoft.Maui.ApplicationModel;
using System.Collections.Generic;
using Microsoft.Maui.Networking; // 🎯 Ajout pour vérifier la connexion Internet

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
        }

        /// <summary>
        /// Méthode requise par IQueryAttributable pour récupérer les paramètres.
        /// </summary>
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
            // Si l'article est déjà téléchargé, utiliser le ContentHtml mis en cache
            if (item.IsDownloaded && !string.IsNullOrWhiteSpace(item.ContentHtml))
            {
                ContentHtml = item.ContentHtml;
                Title = item.Title;
            }
            // 🎯 LOGIQUE CORRIGÉE : Si pas téléchargé, ouvrir le lien externe si en ligne
            else if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
            {
                // Pas téléchargé, mais en ligne : ouvrir dans le navigateur pour une lecture immédiate
                await ExecuteOpenExternalCommand(silent: true);

                // Fournir un feedback dans l'application pendant la transition
                ContentHtml = $"<p><strong>Ouverture de l'article sur le site web...</strong></p>" +
                              $"<p>Appuyez sur 'Retour' pour revenir à l'application.</p>";
            }
            else
            {
                // Pas téléchargé, ET hors ligne : Afficher le résumé et un message d'erreur.
                ContentHtml = $"<p><strong>Article non téléchargé pour la lecture hors ligne.</strong></p>" +
                              $"<p>⚠ Vous êtes hors ligne et l'article n'est pas téléchargé.</p>" +
                              $"<p><em>Résumé :</em> {item.Summary}</p>";
            }
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