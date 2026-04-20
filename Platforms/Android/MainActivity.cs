using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Widget; // Nécessaire pour afficher les messages temporaires (Toast)

namespace Rss_feeder_prout;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    // Variable pour suivre le temps du dernier appui sur le bouton "Retour"
    private long _lastPress;
    // Période maximale entre deux appuis pour quitter (2 secondes)
    private const long DURATION_TO_EXIT_MS = 2000;

    /// <summary>
    /// Intercepte l'appui sur le bouton "Retour" (Back) du système Android.
    /// </summary>
    public override void OnBackPressed()
    {
        // 1. Vérifier si on est sur la MainPage
        var currentPage = Shell.Current.CurrentPage;

        if (currentPage is Rss_feeder_prout.Views.MainPage mainPage)
        {
            // On récupère le ViewModel de la page
            if (mainPage.BindingContext is Rss_feeder_prout.ViewModels.MainViewModel vm)
            {
                // Si le ViewModel est dans un sous-menu (Sites ou Feed), on recule
                if (vm.ShowSiteSelection || vm.ShowFeedView)
                {
                    vm.GoBackToPlaylistsCommand.Execute(null);
                    return; // On arrête ici pour ne pas fermer l'app
                }
            }
        }

        // 2. Si on est sur une autre page (ex: ArticleDetail), on laisse MAUI gérer le retour normal
        if (Shell.Current.Navigation.NavigationStack.Count > 1)
        {
            base.OnBackPressed();
        }
        else
        {
            // 3. Optionnel : Double appui pour quitter si on est vraiment au menu principal
            long currentTime = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (currentTime - _lastPress < 2000)
            {
                Finish();
            }
            else
            {
                _lastPress = currentTime;
                Toast.MakeText(this, "Appuyez à nouveau pour quitter", ToastLength.Short)?.Show();
            }
        }
    }
}
