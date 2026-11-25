using Rss_feeder_prout.ViewModels;

namespace Rss_feeder_prout.Views
{
    public partial class SearchPage : ContentPage
    {
        private readonly SearchViewModel _vm;

        public SearchPage(SearchViewModel vm)
        {
            InitializeComponent();
            BindingContext = _vm = vm;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await _vm.OnAppearingAsync();
            if (!_vm.HasQuery) SearchEntry.Focus();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _vm.Detach();
        }
    }
}
