using Rss_feeder_prout.Services;

namespace Rss_feeder_prout.Controls
{
    public partial class SyncBannerView : ContentView
    {
        public SyncBannerView()
        {
            InitializeComponent();

            // Le bandeau se lie directement au service de synchro (singleton) : visible sur toutes les pages
            var sync = ServiceHelper.Get<SyncService>();
            if (sync != null)
                BindingContext = sync;
            else
                IsVisible = false;
        }
    }
}
