using Microsoft.Maui.Storage;

namespace Rss_feeder_prout.Views;

public partial class OptionRssPage : ContentPage
{
    // Liste des options
    private readonly List<string> _options = new()
    {
        "Aucune mise à jour",
        "5 minutes",
        "2 heures",
        "4 heures",
        "6 heures",
        "8 heures",
        "10 heures",
        "12 heures",
        "24 heures"
    };

    public OptionRssPage()
    {
        InitializeComponent();

        // 1. Remplir le Picker
        IntervalPicker.ItemsSource = _options;

        // 2. Charger la valeur actuelle
        LoadCurrentSettings();
    }

    private void LoadCurrentSettings()
    {
        int totalMinutes = Preferences.Default.Get("RssUpdateIntervalMinutes", 240); // 4h par défaut

        if (totalMinutes == 0)
        {
            IntervalPicker.SelectedIndex = 0;
        }
        else if (totalMinutes == 5)
        {
            IntervalPicker.SelectedIndex = 1;
        }
        else
        {
            // Calcul pour retrouver l'index des heures (totalMinutes / 60)
            string target = $"{totalMinutes / 60} heures";
            IntervalPicker.SelectedItem = _options.FirstOrDefault(o => o == target);
        }
    }

    private void OnIntervalChanged(object sender, EventArgs e)
    {
        if (IntervalPicker.SelectedIndex == -1) return;

        string selectedValue = IntervalPicker.SelectedItem.ToString();
        int totalMinutes = 0;

        if (selectedValue == "5 minutes")
        {
            totalMinutes = 5;
        }
        else if (selectedValue.Contains("heures"))
        {
            int hours = int.Parse(selectedValue.Split(' ')[0]);
            totalMinutes = hours * 60;
        }

        // Sauvegarde
        Preferences.Default.Set("RssUpdateIntervalMinutes", totalMinutes);

        // Redémarrer le timer dans App.xaml.cs
        if (Application.Current is App myApp)
        {
            myApp.ResetTimer();
        }
    }
}