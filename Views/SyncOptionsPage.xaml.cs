using Rss_feeder_prout.Models;
using Rss_feeder_prout.Services;

namespace Rss_feeder_prout.Views
{
    /// <summary>
    /// NOUVEAU : choix avant une synchronisation (flux seuls / tout télécharger, images aucune / optimisées / originales).
    /// Utilisation : var request = await SyncOptionsPage.AskAsync();
    /// </summary>
    public partial class SyncOptionsPage : ContentPage
    {
        private readonly TaskCompletionSource<SyncRequest?> _tcs = new();
        private bool _downloadContent;
        private ImageMode _imageMode;
        private bool _closed;

        public SyncOptionsPage()
        {
            InitializeComponent();

            _downloadContent = AppSettings.ManualDownloadContent;
            _imageMode = AppSettings.ManualImageMode;

            ChipNone.Command = new Command(() => SetImageMode(ImageMode.None));
            ChipOptimized.Command = new Command(() => SetImageMode(ImageMode.Optimized));
            ChipOriginal.Command = new Command(() => SetImageMode(ImageMode.Original));

            Refresh();
        }

        /// <summary>Affiche la feuille (ou retourne directement le choix mémorisé).</summary>
        public static async Task<SyncRequest?> AskAsync()
        {
            if (AppSettings.RememberSyncChoice)
            {
                return new SyncRequest
                {
                    DownloadContent = AppSettings.ManualDownloadContent,
                    ImageMode = AppSettings.ManualImageMode,
                    Trigger = SyncTrigger.Manual,
                    MaxArticles = AppSettings.MaxArticlesPerSync
                };
            }

            var page = new SyncOptionsPage();
            var nav = Shell.Current?.Navigation ?? Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;
            if (nav == null) return null;

            await nav.PushModalAsync(page, false);
            return await page._tcs.Task;
        }

        private void SetImageMode(ImageMode mode)
        {
            _imageMode = mode;
            Refresh();
        }

        private void OnQuickTapped(object? sender, TappedEventArgs e)
        {
            _downloadContent = false;
            Refresh();
        }

        private void OnFullTapped(object? sender, TappedEventArgs e)
        {
            _downloadContent = true;
            Refresh();
        }

        private void Refresh()
        {
            var p = ThemeService.Palette;

            QuickCard.Stroke = new SolidColorBrush(!_downloadContent ? p.Accent : p.Border);
            QuickCard.BackgroundColor = !_downloadContent ? p.AccentSoft : p.Surface;
            QuickIcon.Color = !_downloadContent ? p.Accent : p.TextSecondary;

            FullCard.Stroke = new SolidColorBrush(_downloadContent ? p.Accent : p.Border);
            FullCard.BackgroundColor = _downloadContent ? p.AccentSoft : p.Surface;
            FullIcon.Color = _downloadContent ? p.Accent : p.TextSecondary;

            ImagesSection.IsVisible = _downloadContent;
            ChipNone.IsSelected = _imageMode == ImageMode.None;
            ChipOptimized.IsSelected = _imageMode == ImageMode.Optimized;
            ChipOriginal.IsSelected = _imageMode == ImageMode.Original;

            ImagesHint.Text = _imageMode switch
            {
                ImageMode.None => "Texte seulement : les images s'afficheront en ligne si disponibles.",
                ImageMode.Original => "Images en qualité d'origine (plus d'espace et de données).",
                _ => $"Redimensionnées à {AppSettings.ImageMaxWidth} px et compressées ({AppSettings.ImageQuality} %) : jusqu'à 10× plus léger."
            };
        }

        private async void OnStartClicked(object? sender, EventArgs e)
        {
            AppSettings.ManualDownloadContent = _downloadContent;
            AppSettings.ManualImageMode = _imageMode;
            if (RememberBox.IsChecked) AppSettings.RememberSyncChoice = true;

            await CloseAsync(new SyncRequest
            {
                DownloadContent = _downloadContent,
                ImageMode = _downloadContent ? _imageMode : ImageMode.None,
                Trigger = SyncTrigger.Manual,
                MaxArticles = AppSettings.MaxArticlesPerSync
            });
        }

        private async void OnCancelClicked(object? sender, EventArgs e) => await CloseAsync(null);

        private async void OnBackdropTapped(object? sender, TappedEventArgs e) => await CloseAsync(null);

        private async Task CloseAsync(SyncRequest? result)
        {
            if (_closed) return;
            _closed = true;
            try { await Navigation.PopModalAsync(false); } catch { }
            _tcs.TrySetResult(result);
        }

        protected override bool OnBackButtonPressed()
        {
            _ = CloseAsync(null);
            return true;
        }
    }
}
