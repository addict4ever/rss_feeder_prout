using Rss_feeder_prout.ViewModels;

namespace Rss_feeder_prout.Views
{
    public partial class SourcesPage : ContentPage
    {
        private readonly SourcesViewModel _vm;
        private bool _firstAppearing = true;

        public SourcesPage(SourcesViewModel vm)
        {
            InitializeComponent();
            BindingContext = _vm = vm;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            // Le premier chargement est déclenché par le paramètre de navigation ; ensuite on rafraîchit les compteurs
            if (_firstAppearing) { _firstAppearing = false; return; }
            await _vm.LoadAsync();
        }
    }
}
