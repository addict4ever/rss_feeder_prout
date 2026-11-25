// ArticleDetailPage.xaml.cs
using Rss_feeder_prout.Helpers;
using Rss_feeder_prout.Services;
using Rss_feeder_prout.ViewModels;

namespace Rss_feeder_prout.Views
{
    public partial class ArticleDetailPage : ContentPage
    {
        private readonly ArticleDetailViewModel _vm;

        public ArticleDetailPage(ArticleDetailViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = _vm = viewModel;
        }

        /// <summary>
        /// Les liens touchés dans l'article s'ouvrent dans le navigateur (et pas dans le lecteur).
        /// </summary>
        private async void OnWebViewNavigating(object? sender, WebNavigatingEventArgs e)
        {
            var url = e.Url ?? string.Empty;

            // Lien "Lire sur le site d'origine" du lecteur
            if (url.StartsWith("rssprout://", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                await UiHelper.OpenBrowserAsync(_vm.Article?.Link);
                return;
            }

            // Page du lecteur elle-même (BaseUrl = dossier des images) et pages internes
            if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                var baseUrl = ReaderHtmlBuilder.ReaderBaseUrl;
                if (string.Equals(url.TrimEnd('/'), baseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) return;
                // Lien relatif cassé (résolu vers un fichier) : on bloque au lieu de sortir du lecteur
                e.Cancel = true;
                return;
            }

            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return; // data:, about:

            e.Cancel = true;
            await UiHelper.OpenBrowserAsync(url);
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _vm.OnDisappearing();
        }
    }
}
