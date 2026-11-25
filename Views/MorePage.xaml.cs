using Rss_feeder_prout.Services;

namespace Rss_feeder_prout.Views
{
    public partial class MorePage : ContentPage
    {
        public MorePage()
        {
            InitializeComponent();
            VersionLabel.Text = $"Lecteur RSS · version {AppInfo.Current.VersionString}";
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            ThemeRow.Subtitle = AppSettings.AppTheme switch
            {
                "light" => "Clair",
                "system" => "Comme le système",
                _ => "Sombre"
            };
            var log = CrashLog.Read();
            CrashRow.Subtitle = string.IsNullOrWhiteSpace(log)
                ? "Aucune erreur enregistrée"
                : $"{log.Split("\n[").Length} erreur(s) · à m'envoyer en cas de plantage";
        }

        private async void OnCrashLogTapped(object? sender, EventArgs e)
        {
            var log = CrashLog.Read();
            if (string.IsNullOrWhiteSpace(log))
            {
                await UiHelper.AlertAsync("Journal des erreurs", "Aucune erreur enregistrée.");
                return;
            }

            var choice = await UiHelper.ActionSheetAsync("Journal des erreurs", "Fermer", "Effacer le journal", "Voir les dernières erreurs", "Partager le fichier");
            switch (choice)
            {
                case "Voir les dernières erreurs":
                    await UiHelper.AlertAsync("Dernières erreurs", log.Length > 2500 ? "…" + log.Substring(log.Length - 2500) : log);
                    break;
                case "Partager le fichier":
                    await BackupService.ShareFileAsync(CrashLog.FilePath, "Journal des erreurs Rss Prout");
                    break;
                case "Effacer le journal":
                    CrashLog.Clear();
                    OnAppearing();
                    break;
            }
        }

        private async void OnPlaylistsTapped(object? sender, EventArgs e) => await UiHelper.GoToAsync(nameof(PlaylistManagerPage));
        private async void OnStatsTapped(object? sender, EventArgs e) => await UiHelper.GoToAsync(nameof(StatsPage));
        private async void OnSettingsTapped(object? sender, EventArgs e) => await UiHelper.GoToAsync(nameof(OptionRssPage));
        private async void OnDatabaseTapped(object? sender, EventArgs e) => await UiHelper.GoToAsync(nameof(DatabaseManagerPage));

        private async void OnThemeTapped(object? sender, EventArgs e)
        {
            var choice = await UiHelper.ActionSheetAsync("Thème de l'application", "Annuler", null, "Sombre", "Clair", "Comme le système");
            if (choice == null) return;
            var mode = choice switch { "Clair" => "light", "Comme le système" => "system", _ => "dark" };
            AppSettings.AppTheme = mode;
            ThemeService.Apply(mode);
            OnAppearing();
        }

        private async void OnWebsiteTapped(object? sender, EventArgs e) => await AppShell.OpenWebsiteAsync();

        private async void OnExitTapped(object? sender, EventArgs e)
        {
            if (await UiHelper.ConfirmAsync("Quitter", "Fermer Rss Prout ?", "Quitter", "Annuler"))
                AppShell.ExitApp();
        }
    }
}
