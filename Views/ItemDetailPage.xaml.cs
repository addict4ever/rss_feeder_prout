using Rss_feeder_prout.ViewModels;
using System.ComponentModel;

namespace Rss_feeder_prout.Views
{
    public partial class ItemDetailPage : ContentPage
    {
        private readonly ItemDetailViewModel _vm;

        public ItemDetailPage(ItemDetailViewModel vm)
        {
            InitializeComponent();
            BindingContext = _vm = vm;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ItemDetailViewModel.Url))
                LoadUrl();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            if (PageWebView.Source == null) LoadUrl();
        }

        private void LoadUrl()
        {
            if (string.IsNullOrWhiteSpace(_vm.Url)) return;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _vm.IsLoadingPage = true;
                _vm.Progress = 0.2;
                PageWebView.Source = new UrlWebViewSource { Url = _vm.Url };
            });
        }

        private void OnNavigating(object? sender, WebNavigatingEventArgs e)
        {
            _vm.IsLoadingPage = true;
            _vm.Progress = 0.35;
        }

        private void OnNavigated(object? sender, WebNavigatedEventArgs e)
        {
            _vm.Progress = 1;
            _vm.IsLoadingPage = false;
        }

        private void OnReloadClicked(object? sender, EventArgs e)
        {
            PageWebView.Reload();
        }

        protected override bool OnBackButtonPressed()
        {
            if (PageWebView.CanGoBack)
            {
                PageWebView.GoBack();
                return true;
            }
            return base.OnBackButtonPressed();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }
    }
}
