// Views/PlaylistManagerPage.xaml.cs
using Rss_feeder_prout.ViewModels;

namespace Rss_feeder_prout.Views
{
    public partial class PlaylistManagerPage : ContentPage
    {
        public PlaylistManagerPage(PlaylistManagerViewModel vm)
        {
            InitializeComponent();
            BindingContext = vm;
        }
    }
}