using Rss_feeder_prout.Views;
using System.Diagnostics;

namespace Rss_feeder_prout
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            // --- Pages ouvertes par-dessus les onglets (navigation "push") ---
            Routing.RegisterRoute(nameof(SourcesPage), typeof(SourcesPage));
            Routing.RegisterRoute(nameof(ArticleListPage), typeof(ArticleListPage));
            Routing.RegisterRoute(nameof(ArticleDetailPage), typeof(ArticleDetailPage));
            Routing.RegisterRoute(nameof(ItemDetailPage), typeof(ItemDetailPage));
            Routing.RegisterRoute(nameof(PlaylistManagerPage), typeof(PlaylistManagerPage));
            Routing.RegisterRoute(nameof(PlaylistDetailPage), typeof(PlaylistDetailPage));
            Routing.RegisterRoute(nameof(DatabaseManagerPage), typeof(DatabaseManagerPage));
            Routing.RegisterRoute(nameof(OptionRssPage), typeof(OptionRssPage));
            Routing.RegisterRoute(nameof(StatsPage), typeof(StatsPage));

            // Outils de développement
            Routing.RegisterRoute(nameof(SourceCodePage), typeof(SourceCodePage));
        }

        /// <summary>Ouvre le site web externe.</summary>
        public static async Task OpenWebsiteAsync()
        {
            try
            {
                Uri uri = new("https://www.pouetpouet.ca");
                await Launcher.Default.OpenAsync(uri);
            }
            catch (Exception ex)
            {
                if (Shell.Current != null)
                    await Shell.Current.DisplayAlert("Erreur", $"Impossible d'ouvrir le site : {ex.Message}", "OK");
            }
        }

        /// <summary>Ferme l'application selon la plateforme.</summary>
        public static void ExitApp()
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
                Environment.Exit(0);
            }
        }
    }
}
