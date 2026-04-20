// Views/MainPage.xaml.cs
using Rss_feeder_prout.ViewModels;

namespace Rss_feeder_prout.Views
{
    public partial class MainPage : ContentPage
    {
        // Utilise le namespace du projet
        public MainPage(MainViewModel vm)
        {
            InitializeComponent();
            // Le BindingContext est la connexion entre la vue et le ViewModel
            BindingContext = vm;
        }
    }
}