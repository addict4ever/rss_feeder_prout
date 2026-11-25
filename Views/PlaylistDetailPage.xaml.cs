using Rss_feeder_prout.Services;
using Rss_feeder_prout.ViewModels;

namespace Rss_feeder_prout.Views
{
    public partial class PlaylistDetailPage : ContentPage
    {
        private readonly PlaylistDetailViewModel _vm;

        public PlaylistDetailPage(PlaylistDetailViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = _vm = viewModel;
        }

        private async void OnActiveToggled(object? sender, ToggledEventArgs e)
        {
            // Enregistre immédiatement l'état "inclure dans la synchro"
            if (_vm.CurrentPlaylist != null && _vm.CurrentPlaylist.Id > 0)
            {
                var db = ServiceHelper.Get<SQLiteService>();
                if (db != null) await db.SavePlaylistAsync(_vm.CurrentPlaylist);
            }
        }
    }
}
