using Rss_feeder_prout.ViewModels;

namespace Rss_feeder_prout.Views;

/// <summary>
/// Réglages (anciennement : seulement l'intervalle de mise à jour).
/// La planification est maintenant gérée par Services/SyncScheduler (rattrapage + tâche Android).
/// </summary>
public partial class OptionRssPage : ContentPage
{
    public OptionRssPage(SettingsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
