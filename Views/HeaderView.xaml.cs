using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Devices;

#if ANDROID
using Android.App;
#endif

namespace Rss_feeder_prout.Views;

public partial class HeaderView : ContentView
{
    private readonly Random _random = new();
    private readonly PathGeometryConverter _converter = new();

    // Liste des expressions possibles (Bouche)
    private readonly string[] _randomFaces = new[]
{
    // --- LES SOURIRES (1-7) ---
    "M 20,45 Q 35,60 50,45",           // 01. Sourire standard
    "M 15,45 Q 35,70 55,45",           // 02. Très grand sourire profond
    "M 20,50 Q 35,60 50,50",           // 03. Petit sourire discret
    "M 20,45 C 25,55 45,55 50,45",     // 04. Sourire doux (Courbe de Bézier)
    "M 15,50 Q 25,65 55,50",           // 05. Sourire étiré
    "M 20,45 Q 45,60 50,40",           // 06. Sourire en coin (droite)
    "M 20,40 Q 25,60 50,45",           // 07. Sourire en coin (gauche)

    // --- LES NEUTRES ET DROITS (8-12) ---
    "M 20,55 L 50,55",                 // 08. Ligne droite (Sérieux)
    "M 25,55 L 45,55",                 // 09. Petite ligne (Bof)
    "M 15,55 L 55,55",                 // 10. Longue ligne (Blasé)
    "M 20,53 L 50,57",                 // 11. Ligne de travers / sceptique
    "M 20,55 Q 35,55 50,55",           // 12. Plat mais légèrement souple

    // --- LES MÉCONTENTS / TRISTES (13-18) ---
    "M 20,60 Q 35,45 50,60",           // 13. Triste (arc vers le haut)
    "M 15,65 Q 35,40 55,65",           // 14. Très mécontent
    "M 25,55 Q 35,50 45,55",           // 15. Petite moue déçue
    "M 20,50 Q 35,40 50,50",           // 16. Air malicieux inversé
    "M 20,60 L 35,50 L 50,60",         // 17. Bouche en "V" inversé (fâché)
    "M 20,55 C 20,45 50,45 50,55",     // 18. Triste doux

    // --- LES RIGOLOS / SPÉCIAUX (19-25) ---
    "M 25,50 C 25,65 45,65 45,50",     // 19. Petite moue ronde (Cute)
    "M 20,50 Q 25,40 35,50 Q 45,60 55,50", // 20. Bouche en vague (Siffle)
    "M 30,55 A 5,5 0 1,0 40,55",       // 21. Petit demi-cercle (étonné léger)
    "M 20,50 L 30,60 L 40,50 L 50,60", // 22. Zig-zag (Incertain)
    "M 20,50 Q 35,75 35,50",           // 23. Demi-sourire bizarre
    "M 20,50 Q 20,65 50,65",           // 24. Grand crochet
    "M 30,60 Q 35,50 40,60"            // 25. Petit bec (Bisou)
};

    public HeaderView()
    {
        InitializeComponent();
        ApplyVisibilityLogic();
    }

    private void ApplyVisibilityLogic()
    {
        if (DeviceInfo.Current.Idiom == DeviceIdiom.Tablet)
        {
            if (IsGestureNavigationActive())
            {
                MenuButton.IsVisible = true;
            }
        }
    }

    private bool IsGestureNavigationActive()
    {
#if ANDROID
        try
        {
            var context = Android.App.Application.Context;
            var resources = context.Resources;
            int resourceId = resources.GetIdentifier("config_navBarInteractionMode", "integer", "android");
            if (resourceId > 0)
            {
                return resources.GetInteger(resourceId) == 2;
            }
        }
        catch { }
#endif
        return DeviceInfo.Current.Idiom == DeviceIdiom.Tablet;
    }

    private void OnMenuButtonClicked(object sender, EventArgs e)
    {
        bool isOpening = !Shell.Current.FlyoutIsPresented;
        Shell.Current.FlyoutIsPresented = isOpening;

        if (isOpening)
        {
            // Quand on OUVRE : Toujours la bouche "O" (Surprise)
            SmileyMouth.Data = (PathGeometry)_converter.ConvertFromInvariantString("M 30,50 A 5,5 0 1,0 40,50 A 5,5 0 1,0 30,50");
        }
        else
        {
            // Quand on FERME : On choisit une face au hasard parmi la liste
            int index = _random.Next(_randomFaces.Length);
            string randomPath = _randomFaces[index];
            SmileyMouth.Data = (PathGeometry)_converter.ConvertFromInvariantString(randomPath);
        }
    }
}