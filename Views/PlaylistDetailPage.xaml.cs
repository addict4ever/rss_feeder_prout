using Microsoft.Maui.Controls;
using Rss_feeder_prout.ViewModels;

namespace Rss_feeder_prout.Views
{
    // Le 'partial' est ce qui lie ce fichier à la définition de la classe dans PlaylistDetailPage.xaml
    public partial class PlaylistDetailPage : ContentPage
    {
        // Le constructeur prend le ViewModel en argument grâce à l'injection de dépendances (MauiProgram.cs)
        public PlaylistDetailPage(PlaylistDetailViewModel viewModel)
        {
            InitializeComponent();

            // Lier le ViewModel à la page
            this.BindingContext = viewModel;
        }
    }
}