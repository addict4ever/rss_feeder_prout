// Dans Rss_feeder_prout\Views\DatabaseManagerPage.xaml.cs

using Microsoft.Maui.Controls;
using Rss_feeder_prout.ViewModels;

namespace Rss_feeder_prout.Views
{
    // 🎯 Assurez-vous que la classe est déclarée comme public et hérite de ContentPage
    public partial class DatabaseManagerPage : ContentPage
    {
        public DatabaseManagerPage(DatabaseManagerViewModel viewModel)
        {
            InitializeComponent();
            // Le BindingContext est défini ici par injection de dépendances
            this.BindingContext = viewModel;
        }
    }
}