using Rss_feeder_prout.ViewModels;

namespace Rss_feeder_prout.Views
{
    public partial class ArchivePage : ContentPage
    {
        private readonly ArchiveViewModel _viewModel;

        public ArchivePage(ArchiveViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = _viewModel = viewModel;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            // Déclenche le chargement des archives
            await _viewModel.ExecuteLoadArchivesCommand();
        }
    }
}
