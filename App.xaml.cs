using Rss_feeder_prout.Services;
using System.Diagnostics;
using Microsoft.Maui.Storage; // Nécessaire pour Preferences

namespace Rss_feeder_prout
{
    public partial class App : Application
    {
        // Référence au timer pour éviter qu'il ne soit supprimé par le ramasse-miettes
        private IDispatcherTimer _syncTimer;

        public App()
        {
            InitializeComponent();

            // On lance le timer dès le démarrage
            StartBackgroundSyncTimer();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }

        private void StartBackgroundSyncTimer()
        {
            int totalMinutes = Preferences.Default.Get("RssUpdateIntervalMinutes", 240);

            if (totalMinutes <= 0)
            {
                Debug.WriteLine("[TIMER] Mise à jour automatique désactivée.");
                return;
            }

            _syncTimer = Dispatcher.CreateTimer();
            _syncTimer.Interval = TimeSpan.FromMinutes(totalMinutes);

            _syncTimer.Tick += async (s, e) =>
            {
                if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                {
                    Debug.WriteLine("[TIMER] Pas d'internet, on saute ce cycle.");
                    return;
                }

#if ANDROID || IOS || WINDOWS
                var scope = IPlatformApplication.Current.Services;
                var rssService = scope.GetService<RssService>();
                var dbService = scope.GetService<SQLiteService>();
#else
        var rssService = Handler?.MauiContext?.Services.GetService<RssService>();
        var dbService = Handler?.MauiContext?.Services.GetService<SQLiteService>();
#endif

                if (rssService != null && dbService != null)
                {
                    try
                    {
                        var playlists = await dbService.GetPlaylistsAsync();

                        if (playlists != null && playlists.Any())
                        {
                            foreach (var playlist in playlists)
                            {
                                if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet) break;

                                Debug.WriteLine($"[TIMER] Synchro en cours : {playlist.Name}");
                                await rssService.SynchronizeSitesInPlaylistAsync(playlist.Id);
                            }

                            Debug.WriteLine("[TIMER] Cycle terminé avec succès.");

                            // 🎯 MESSAGE DE CONFIRMATION À LA FIN
                            // On force l'exécution sur le Thread UI pour afficher la MessageBox
                            MainThread.BeginInvokeOnMainThread(async () =>
                            {
                                if (MainPage != null)
                                {
                                    await MainPage.DisplayAlert("Mise à jour terminée",
                                        "Les flux RSS ont été synchronisés avec succès. Les images sont prêtes pour le mode hors-ligne.",
                                        "Super !");
                                }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[TIMER ERROR] : {ex.Message}");

                        MainThread.BeginInvokeOnMainThread(async () =>
                        {
                            if (MainPage != null)
                                await MainPage.DisplayAlert("Sync Auto", "Erreur lors de la mise à jour : " + ex.Message, "OK");
                        });
                    }
                }
                else
                {
                    Debug.WriteLine("[TIMER] Erreur : Services non trouvés.");
                }
            };

            _syncTimer.IsRepeating = true;
            _syncTimer.Start();

            Debug.WriteLine($"[TIMER] Lancé avec un intervalle de {totalMinutes} minutes.");
        }

        /// <summary>
        /// Permet de redémarrer le timer immédiatement après un changement dans les options
        /// </summary>
        public void ResetTimer()
        {
            if (_syncTimer != null)
            {
                _syncTimer.Stop();
                _syncTimer = null; // Nettoyage
            }
            StartBackgroundSyncTimer();
        }
    }
}