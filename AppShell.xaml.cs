using Rss_feeder_prout.Views;
using System.Diagnostics;

namespace Rss_feeder_prout
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            // --- Enregistrement des routes ---

            // 1. Pages principales
            Routing.RegisterRoute(nameof(MainPage), typeof(MainPage));
            Routing.RegisterRoute(nameof(PlaylistManagerPage), typeof(PlaylistManagerPage));

            // 2. Pages de détails (utilisées pour la navigation push)
            Routing.RegisterRoute(nameof(PlaylistDetailPage), typeof(PlaylistDetailPage));

            // On enregistre ArticleDetailPage (nom utilisé dans tes ViewModels)
            Routing.RegisterRoute(nameof(ArticleDetailPage), typeof(ArticleDetailPage));

            // Si tu utilises encore ItemDetailPage par endroits, décommente la ligne suivante :
            // Routing.RegisterRoute(nameof(ItemDetailPage), typeof(ItemDetailPage));
        }

        // --- Gestion des événements du Flyout (Menu) ---

        /// <summary>
        /// Ouvre le site web externe
        /// </summary>
        private async void OnOpenWebsiteClicked(object sender, EventArgs e)
        {
            try
            {
                Uri uri = new("https://www.pouetpouet.ca");
                await Launcher.Default.OpenAsync(uri);
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Erreur", $"Impossible d'ouvrir le site : {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Ferme l'application selon la plateforme
        /// </summary>
        private void OnExitClicked(object sender, EventArgs e)
        {
            try
            {
#if ANDROID
                Android.OS.Process.KillProcess(Android.OS.Process.MyPid());
#elif WINDOWS
                Microsoft.UI.Xaml.Application.Current.Exit();
#elif MACCATALYST
                Environment.Exit(0);
#else
                Process.GetCurrentProcess().Kill(); 
#endif
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur lors de la fermeture : {ex.Message}");
                // Fallback ultime
                Environment.Exit(0);
            }
        }
    }
}