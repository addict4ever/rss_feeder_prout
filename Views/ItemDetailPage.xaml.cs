// Dans Views/ItemDetailPage.xaml.cs
using Rss_feeder_prout.ViewModels;
using Microsoft.Maui.Controls;

namespace Rss_feeder_prout.Views
{
    // 🎯 CORRECTION : Assurez-vous d'avoir le mot-clé 'partial'
    public partial class ItemDetailPage : ContentPage
    {
        public ItemDetailPage(ItemDetailViewModel viewModel)
        {
            InitializeComponent();
            this.BindingContext = viewModel;
        }
    }
}