using Rss_feeder_prout.ViewModels;

namespace Rss_feeder_prout.Views
{
    public partial class FavoritesPage : ContentPage
    {
        private readonly FavoritesViewModel _vm;

        public FavoritesPage(FavoritesViewModel vm)
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
