using Rss_feeder_prout.ViewModels;

namespace Rss_feeder_prout.Views
{
    public partial class ArticleListPage : ContentPage
    {
        private readonly ArticleListViewModel _vm;

        public ArticleListPage(ArticleListViewModel vm)
        {
            InitializeComponent();
            BindingContext = _vm = vm;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await _vm.OnAppearingAsync();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _vm.Detach();
        }
    }
}
