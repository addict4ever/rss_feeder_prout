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

        private void OnMenuButtonClicked(object sender, EventArgs e)
        {
            // On inverse l'état actuel : si c'est vrai, ça devient faux, et inversement
            Shell.Current.FlyoutIsPresented = !Shell.Current.FlyoutIsPresented;
        }
    }
}