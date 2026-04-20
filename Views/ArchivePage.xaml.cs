using Rss_feeder_prout.ViewModels;

namespace Rss_feeder_prout.Views
{
    public partial class ArchivePage : ContentPage
    {
        ArchiveViewModel _viewModel;

        public ArchivePage(ArchiveViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = _viewModel = viewModel;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            // Déclenche le chargement des archives
            _viewModel.LoadArchivesCommand.Execute(null);
        }
    }
}