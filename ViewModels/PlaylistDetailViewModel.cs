using Rss_feeder_prout.Models;
using Rss_feeder_prout.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Diagnostics;
using Microsoft.Maui.ApplicationModel;

namespace Rss_feeder_prout.ViewModels
{
    /// <summary>
    /// Sources d'une catégorie : ajout avec DÉCOUVERTE AUTOMATIQUE du flux (URL de site acceptée),
    /// modification (nom, URL, sélecteurs CSS), déplacement, test / santé, pause.
    /// </summary>
    [QueryProperty(nameof(PlaylistId), "id")]
    public class PlaylistDetailViewModel : BaseViewModel
    {
        private readonly SQLiteService _dbService;
        private readonly RssService _rssService;

        private FeedPlaylist _currentPlaylist = new();
        public FeedPlaylist CurrentPlaylist
        {
            get => _currentPlaylist;
            set => SetProperty(ref _currentPlaylist, value);
        }

        public ObservableCollection<FeedSite> Sites { get; } = new ObservableCollection<FeedSite>();

        private string _newUrlText = string.Empty;
        public string NewUrlText
        {
            get => _newUrlText;
            set
            {
                if (SetProperty(ref _newUrlText, value ?? string.Empty))
                    ((Command)AddSiteCommand).ChangeCanExecute();
            }
        }

        private int _playlistId;
        public int PlaylistId
        {
            get => _playlistId;
            set
            {
                if (SetProperty(ref _playlistId, value))
                    _ = LoadPlaylistAndSites();
            }
        }

        private string _statusText = string.Empty;
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public string SitesCountText => Sites.Count == 1 ? "1 source" : $"{Sites.Count} sources";

        public ICommand SaveCommand { get; }
        public ICommand AddSiteCommand { get; }
        public ICommand DeleteSiteCommand { get; }
        public ICommand EditSiteCommand { get; }
        public ICommand TestAllCommand { get; }
        public ICommand RenameCommand { get; }
        public ICommand ToggleActiveCommand { get; }

        public PlaylistDetailViewModel(SQLiteService dbService, RssService rssService)
        {
            _dbService = dbService;
            _rssService = rssService;

            SaveCommand = new Command(async () => await ExecuteSaveCommand());
            AddSiteCommand = new Command(async () => await ExecuteAddSiteCommand(), CanExecuteAddSite);
            DeleteSiteCommand = new Command<FeedSite>(async (s) => await ExecuteDeleteSiteCommand(s));
            EditSiteCommand = new Command<FeedSite>(async (s) => await EditSiteAsync(s));
            TestAllCommand = new Command(async () => await TestAllAsync());
            RenameCommand = new Command(async () => await RenameAsync());
            ToggleActiveCommand = new Command(async () =>
            {
                CurrentPlaylist.IsActive = !CurrentPlaylist.IsActive;
                await _dbService.SavePlaylistAsync(CurrentPlaylist);
                UiHelper.Toast(CurrentPlaylist.IsActive ? "Synchronisation activée" : "Synchronisation en pause");
            });
        }

        private async Task LoadPlaylistAndSites()
        {
            IsBusy = true;
            try
            {
                var playlist = await _dbService.GetPlaylistAsync(PlaylistId);
                if (playlist != null)
                {
                    CurrentPlaylist = playlist;
                    Title = playlist.Name;
                }

                var sitesList = await _dbService.GetSitesForPlaylistAsync(PlaylistId);
                var totals = await _dbService.GetTotalCountsBySiteAsync();
                foreach (var s in sitesList) s.TotalCount = totals.TryGetValue(s.Id, out var t) ? t : 0;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Sites.Clear();
                    foreach (var site in sitesList) Sites.Add(site);
                    OnPropertyChanged(nameof(SitesCountText));
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] Failed to load playlist ID {PlaylistId} or sites: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ------------------------------------------------------------------
        // Ajout avec découverte automatique
        // ------------------------------------------------------------------

        private bool CanExecuteAddSite() => !string.IsNullOrWhiteSpace(NewUrlText);

        private async Task ExecuteAddSiteCommand()
        {
            if (!CanExecuteAddSite() || IsBusy) return;

            var input = NewUrlText.Trim();
            IsBusy = true;
            StatusText = "Recherche du flux…";

            try
            {
                var feeds = await _rssService.DiscoverFeedsAsync(input);
                if (feeds.Count == 0)
                {
                    await UiHelper.AlertAsync("Aucun flux trouvé", "Impossible de trouver un flux RSS / Atom à cette adresse. Vérifiez l'URL.");
                    return;
                }

                var chosen = feeds[0];
                if (feeds.Count > 1)
                {
                    var labels = feeds.Select(f => string.IsNullOrWhiteSpace(f.Title) ? f.Url : $"{f.Title} — {f.Url}").ToArray();
                    var pick = await UiHelper.ActionSheetAsync("Plusieurs flux trouvés", "Annuler", null, labels);
                    if (pick == null) return;
                    chosen = feeds[Array.IndexOf(labels, pick)];
                }

                // Doublons (toutes catégories)
                var existing = await _dbService.FindSiteByUrlAsync(chosen.Url);
                if (existing != null)
                {
                    var where = existing.PlaylistId == PlaylistId ? "dans cette catégorie" : "dans une autre catégorie";
                    await UiHelper.AlertAsync("Déjà présent", $"Ce flux est déjà enregistré {where} (« {existing.DisplayName} »).");
                    return;
                }

                var name = !string.IsNullOrWhiteSpace(chosen.Title) ? chosen.Title.Trim() : new Uri(chosen.Url).Host.Replace("www.", "");
                var newSite = new FeedSite
                {
                    Name = name,
                    FeedUrl = chosen.Url,
                    PlaylistId = CurrentPlaylist.Id,
                    SortOrder = Sites.Count + 1
                };

                await _dbService.SaveSiteAsync(newSite);
                Sites.Add(newSite);
                OnPropertyChanged(nameof(SitesCountText));
                NewUrlText = string.Empty;
                UiHelper.Toast($"« {name} » ajouté");

                // Icône + première récupération en arrière-plan
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _rssService.UpdateSiteFeedAsync(newSite, AppSettings.ManualImageMode);
                        await _rssService.EnsureSiteIconAsync(newSite);
                    }
                    catch (Exception ex) { Debug.WriteLine($"[ADD SITE] {ex.Message}"); }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RSS ERROR] Erreur lors de la validation du flux : {ex.Message}");
                await UiHelper.AlertAsync("Erreur", "Impossible de valider ce flux RSS. Vérifiez l'URL.");
            }
            finally
            {
                IsBusy = false;
                StatusText = string.Empty;
            }
        }

        // ------------------------------------------------------------------
        // Édition d'une source
        // ------------------------------------------------------------------

        private async Task EditSiteAsync(FeedSite? site)
        {
            if (site == null) return;

            var actions = new[]
            {
                "Tester le flux",
                "Renommer",
                "Modifier l'URL du flux",
                "Sélecteur du contenu (CSS)",
                "Éléments à retirer (CSS)",
                "Déplacer vers une autre catégorie",
                site.IsDisabled ? "Réactiver la source" : "Mettre en pause",
                site.AlwaysDownloadFull ? "✓ Toujours télécharger l'article complet" : "Toujours télécharger l'article complet",
                site.OpenInBrowser ? "✓ Ouvrir dans le navigateur" : "Ouvrir dans le navigateur (pas de mode lecture)",
                "Réinitialiser l'icône"
            };

            var choice = await UiHelper.ActionSheetAsync(site.DisplayName, "Annuler", "Supprimer", actions);
            if (choice == null) return;

            switch (choice)
            {
                case "Tester le flux":
                    site.IsBusy = true;
                    var (ok, msg) = await _rssService.TestFeedAsync(site);
                    site.IsBusy = false;
                    site.RefreshHealth();
                    await UiHelper.AlertAsync(ok ? "✅ Flux valide" : "❌ Problème", msg);
                    return;

                case "Renommer":
                    var name = await UiHelper.PromptAsync("Renommer", "Nom de la source :", site.Name);
                    if (!string.IsNullOrWhiteSpace(name)) site.Name = name.Trim();
                    break;

                case "Modifier l'URL du flux":
                    var url = await UiHelper.PromptAsync("URL du flux", "Adresse RSS / Atom :", site.FeedUrl, keyboard: Keyboard.Url);
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        site.FeedUrl = RssService.NormalizeUrl(url);
                        site.ETag = string.Empty;
                        site.LastModified = string.Empty;
                    }
                    break;

                case "Sélecteur du contenu (CSS)":
                    var content = await UiHelper.PromptAsync("Zone de contenu",
                        "Sélecteur CSS de l'article (laisser vide = détection automatique).\nEx. : article .entry-content",
                        site.ContentSelector);
                    if (content != null) site.ContentSelector = content.Trim();
                    break;

                case "Éléments à retirer (CSS)":
                    var clean = await UiHelper.PromptAsync("Éléments à retirer",
                        "Sélecteurs CSS séparés par des virgules.\nEx. : .ads, .social-share, aside",
                        site.CustomCleanSelectors);
                    if (clean != null) site.CustomCleanSelectors = clean.Trim();
                    break;

                case "Déplacer vers une autre catégorie":
                    var playlists = (await _dbService.GetPlaylistsAsync()).Where(p => p.Id != site.PlaylistId).ToList();
                    if (playlists.Count == 0) return;
                    var target = await UiHelper.ActionSheetAsync("Déplacer vers…", "Annuler", null, playlists.Select(p => p.Name).ToArray());
                    var dest = playlists.FirstOrDefault(p => p.Name == target);
                    if (dest != null)
                    {
                        await _dbService.MoveSiteAsync(site, dest.Id);
                        Sites.Remove(site);
                        OnPropertyChanged(nameof(SitesCountText));
                        UiHelper.Toast($"Déplacé vers « {dest.Name} »");
                    }
                    return;

                case "Réactiver la source":
                case "Mettre en pause":
                    site.IsDisabled = !site.IsDisabled;
                    break;

                case "✓ Toujours télécharger l'article complet":
                case "Toujours télécharger l'article complet":
                    site.AlwaysDownloadFull = !site.AlwaysDownloadFull;
                    break;

                case "✓ Ouvrir dans le navigateur":
                case "Ouvrir dans le navigateur (pas de mode lecture)":
                    site.OpenInBrowser = !site.OpenInBrowser;
                    break;

                case "Réinitialiser l'icône":
                    site.LocalIconPath = string.Empty;
                    site.IconUrl = string.Empty;
                    await _rssService.EnsureSiteIconAsync(site);
                    break;

                case "Supprimer":
                    await ExecuteDeleteSiteCommand(site);
                    return;
            }

            await _dbService.UpdateSiteAsync(site);
            site.RefreshHealth();
        }

        private async Task ExecuteDeleteSiteCommand(FeedSite? site)
        {
            if (site == null) return;
            if (!await UiHelper.ConfirmAsync("Supprimer la source", $"Supprimer « {site.DisplayName} » et ses articles ?", "Supprimer", "Annuler")) return;

            await _dbService.DeleteSiteAsync(site);
            Sites.Remove(site);
            OnPropertyChanged(nameof(SitesCountText));
        }

        private async Task TestAllAsync()
        {
            if (IsBusy || Sites.Count == 0) return;
            IsBusy = true;
            int ok = 0, ko = 0, done = 0;
            try
            {
                using var gate = new SemaphoreSlim(5);
                var tasks = Sites.ToList().Select(async s =>
                {
                    await gate.WaitAsync();
                    try
                    {
                        MainThread.BeginInvokeOnMainThread(() => s.IsBusy = true);
                        var (good, _) = await _rssService.TestFeedAsync(s);
                        if (good) Interlocked.Increment(ref ok); else Interlocked.Increment(ref ko);
                    }
                    finally
                    {
                        int d = Interlocked.Increment(ref done);
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            s.IsBusy = false;
                            s.RefreshHealth();
                            StatusText = $"Test {d}/{Sites.Count}…";
                        });
                        gate.Release();
                    }
                }).ToList();
                await Task.WhenAll(tasks);
                await UiHelper.AlertAsync("Santé des flux", $"{ok} flux OK · {ko} en erreur.");
            }
            finally
            {
                IsBusy = false;
                StatusText = string.Empty;
            }
        }

        private async Task RenameAsync()
        {
            var name = await UiHelper.PromptAsync("Renommer la catégorie", "Nouveau nom :", CurrentPlaylist.Name);
            if (string.IsNullOrWhiteSpace(name)) return;
            CurrentPlaylist.Name = name.Trim();
            Title = CurrentPlaylist.Name;
            await _dbService.SavePlaylistAsync(CurrentPlaylist);
        }

        private async Task ExecuteSaveCommand()
        {
            if (IsBusy || CurrentPlaylist == null) return;

            if (string.IsNullOrWhiteSpace(CurrentPlaylist.Name))
            {
                await UiHelper.AlertAsync("Erreur", "Le nom de la catégorie ne peut être vide.");
                return;
            }

            await _dbService.SavePlaylistAsync(CurrentPlaylist);
            UiHelper.Toast("Catégorie enregistrée");
            await UiHelper.GoBackAsync();
        }
    }
}
