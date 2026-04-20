using Microsoft.Maui.Controls;
using Rss_feeder_prout.Models;
using Rss_feeder_prout.Services;
using System.Diagnostics;
using System.Windows.Input;
using System;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;

namespace Rss_feeder_prout.ViewModels
{
    // 🎯 CORRECTION : QueryProperty doit correspondre au paramètre d'URL utilisé dans MainViewModel ('itemId', non 'id')
    [QueryProperty(nameof(ItemId), "itemId")]
    public class ItemDetailViewModel : BaseViewModel
    {
        private readonly SQLiteService _dbService;
        private readonly RssService _rssService; // 🎯 Ajout du service RSS

        // --- Propriétés liées au Navigation (Shell) ---

        private int _itemId;
        public int ItemId
        {
            get => _itemId;
            set
            {
                if (SetProperty(ref _itemId, value))
                {
                    // Dès que l'ID est défini par Shell, chargez l'article
                    Task.Run(LoadItemAsync);
                }
            }
        }

        // --- Propriétés liées à l'UI ---

        private RssItem _rssItem;
        public RssItem RssItem
        {
            get => _rssItem;
            // 🎯 Utiliser SetProperty pour garantir la notification UI (bien que RssItem soit un objet entier, c'est mieux)
            set => SetProperty(ref _rssItem, value);
        }

        // 🎯 Nouvelle propriété pour afficher le contenu (Summary ou SavedContent)
        public string DisplayContent => RssItem?.ContentHtml ?? RssItem?.Summary;
        public bool IsContentDownloaded => !string.IsNullOrWhiteSpace(RssItem?.ContentHtml);


        public ICommand OpenExternalCommand { get; }
        public ICommand BackCommand { get; }
        public ICommand DownloadContentCommand { get; } // 🎯 Nouvelle Commande

        // --- Constructeur ---

        // 🎯 Ajout de RssService au constructeur pour gérer le téléchargement
        public ItemDetailViewModel(SQLiteService dbService, RssService rssService)
        {
            _dbService = dbService;
            _rssService = rssService; // Initialisation du service RSS
            RssItem = new RssItem();

            OpenExternalCommand = new Command(async () => await ExecuteOpenExternalCommand());
            BackCommand = new Command(async () => await Shell.Current.GoToAsync(".."));

            // 🎯 Nouvelle Commande pour le téléchargement
            DownloadContentCommand = new Command(async () => await ExecuteDownloadContentCommand(), () => RssItem != null && !RssItem.IsDownloaded && !IsBusy);
        }

        // --- Méthodes d'Exécution ---

        private async Task LoadItemAsync()
        {
            if (ItemId <= 0) return;

            try
            {
                IsBusy = true;

                var item = await _dbService.GetRssItemAsync(ItemId);

                if (item != null)
                {
                    RssItem = item;

                    // 🎯 LOGIQUE HORS LIGNE/LECTURE

                    // 1. Marquer comme lu (car l'utilisateur a ouvert l'article)
                    if (!RssItem.IsRead)
                    {
                        RssItem.IsRead = true; // Notifie UI et MainViewModel via INotifyPropertyChanged de RssItem
                        await _dbService.MarkItemAsReadAsync(RssItem);
                    }

                    // 2. Notifier les changements pour les propriétés calculées
                    OnPropertyChanged(nameof(DisplayContent));
                    OnPropertyChanged(nameof(IsContentDownloaded));

                    // 3. Mettre à jour l'état de la commande Download
                    ((Command)DownloadContentCommand).ChangeCanExecute();
                }
                else
                {
                    // L'article n'existe pas dans la DB, navigation retour
                    await Shell.Current.GoToAsync("..");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load RSS Item: {ex.Message}");
                await Shell.Current.DisplayAlert("Erreur", "Impossible de charger les détails de l'article.", "OK");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ExecuteDownloadContentCommand()
        {
            if (RssItem == null || RssItem.IsDownloaded) return;

            try
            {
                // IsBusy du RssItem est utilisé pour l'UI du bouton de téléchargement
                RssItem.IsBusy = true;

                // Le service gère le téléchargement et la mise à jour de la DB et de RssItem.IsDownloaded/SavedContent
                await _rssService.DownloadAndSaveItemContentAsync(RssItem);

                // Forcer la mise à jour des propriétés calculées
                OnPropertyChanged(nameof(DisplayContent));
                OnPropertyChanged(nameof(IsContentDownloaded));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error downloading content: {ex.Message}");
                await Shell.Current.DisplayAlert("Erreur de Téléchargement", "Échec du téléchargement du contenu pour la lecture hors ligne.", "OK");
            }
            finally
            {
                RssItem.IsBusy = false;
                ((Command)DownloadContentCommand).ChangeCanExecute();
            }
        }


        private async Task ExecuteOpenExternalCommand()
        {
            if (RssItem?.Link == null) return;

            try
            {
                // Ouvre le lien de l'article dans le navigateur par défaut
                await Launcher.OpenAsync(RssItem.Link);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open external link: {ex.Message}");
                await Shell.Current.DisplayAlert("Erreur", "Impossible d'ouvrir le lien externe.", "OK");
            }
        }
    }
}