using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Widget; // Nécessaire pour afficher les messages temporaires (Toast)

namespace Rss_feeder_prout;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    // Variable pour suivre le temps du dernier appui sur le bouton "Retour"
    private long _lastPress;
    // Période maximale entre deux appuis pour quitter (2 secondes)
    private const long DURATION_TO_EXIT_MS = 2000;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Rss_feeder_prout.Services.ThemeService.ApplyStatusBar();
    }

    /// <summary>
    /// Intercepte l'appui sur le bouton "Retour" (Back) du système Android.
    /// 1. une page est ouverte par-dessus (article, catégorie…) -> retour normal
    /// 2. on est sur un autre onglet que l'Accueil -> retour à l'Accueil
    /// 3. on est sur l'Accueil -> double appui pour quitter
    /// </summary>
#pragma warning disable CS0672, CA1422
    public override void OnBackPressed()
    {
        var shell = Shell.Current;

        if (shell == null || shell.Navigation.ModalStack.Count > 0 || shell.Navigation.NavigationStack.Count > 1)
        {
            base.OnBackPressed();
            return;
        }

        var tabBar = shell.CurrentItem;
        if (tabBar != null && tabBar.Items.Count > 0 && tabBar.CurrentItem != tabBar.Items[0])
        {
            tabBar.CurrentItem = tabBar.Items[0];
            return;
        }

        long currentTime = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (currentTime - _lastPress < DURATION_TO_EXIT_MS)
        {
            Finish();
        }
        else
        {
            _lastPress = currentTime;
            Toast.MakeText(this, "Appuyez à nouveau pour quitter", ToastLength.Short)?.Show();
        }
    }
#pragma warning restore CS0672, CA1422
}
