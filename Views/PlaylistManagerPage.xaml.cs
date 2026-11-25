// Views/PlaylistManagerPage.xaml.cs
using Rss_feeder_prout.ViewModels;

namespace Rss_feeder_prout.Views
{
    public partial class PlaylistManagerPage : ContentPage
    {
        private readonly PlaylistManagerViewModel _vm;

        public PlaylistManagerPage(PlaylistManagerViewModel vm)
        {
            InitializeComponent();
            BindingContext = _vm = vm;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await _vm.ExecuteLoadPlaylistsCommand();
        }
    }
}
