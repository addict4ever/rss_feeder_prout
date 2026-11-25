using System.ComponentModel;
using Rss_feeder_prout.ViewModels;

namespace Rss_feeder_prout.Views
{
    public partial class DatabaseManagerPage : ContentPage
    {
        private readonly DatabaseManagerViewModel _vm;

        public DatabaseManagerPage(DatabaseManagerViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = _vm = viewModel;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await _vm.RefreshAsync();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
        }

        /// <summary>Barre de répartition de l'espace (largeurs proportionnelles).</summary>
        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not (nameof(DatabaseManagerViewModel.DbRatio) or nameof(DatabaseManagerViewModel.ImgRatio)
                or nameof(DatabaseManagerViewModel.BackupRatio) or nameof(DatabaseManagerViewModel.OtherRatio)))
                return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                var ratios = new[] { _vm.DbRatio, _vm.ImgRatio, _vm.BackupRatio, _vm.OtherRatio };
                for (int i = 0; i < ratios.Length && i < UsageBar.ColumnDefinitions.Count; i++)
                    UsageBar.ColumnDefinitions[i].Width = new GridLength(Math.Max(ratios[i], 0.005), GridUnitType.Star);
            });
        }
    }
}
