// ArticleDetailPage.xaml.cs
using Microsoft.Maui.Controls;
using Rss_feeder_prout.ViewModels;

namespace Rss_feeder_prout
{
    public partial class ArticleDetailPage : ContentPage
    {
        // 🎯 L'attribut [QueryProperty] dans le ViewModel gère la réception de l'ID.
        // Ici, on injecte simplement le ViewModel.
        public ArticleDetailPage(ArticleDetailViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
        }
    }
}