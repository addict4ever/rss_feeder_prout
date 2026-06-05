using System.Text.RegularExpressions;
using Microsoft.Maui.Storage;

namespace Rss_feeder_prout.Views;

// IQueryAttributable est nécessaire pour recevoir les données sans plantage via Shell
public partial class SourceCodePage : ContentPage, IQueryAttributable
{
    private string _currentHtml;

    // Constructeur sans paramètre : OBLIGATOIRE pour la navigation Shell
    public SourceCodePage()
    {
        InitializeComponent();
    }

    // Reçoit le contenu HTML envoyé par le ViewModel
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("HtmlContent", out object content))
        {
            _currentHtml = content?.ToString() ?? string.Empty;
            LoadEditor();
        }
    }

    private void LoadEditor()
    {
        if (string.IsNullOrEmpty(_currentHtml)) return;

        var htmlSource = new HtmlWebViewSource();

        // Template HTML utilisant uniquement les fichiers locaux
        htmlSource.Html = $@"
    <html>
    <head>
        <meta name='viewport' content='width=device-width, initial-scale=1.0'>
        <link rel='stylesheet' href='prism.css'>
        <link rel='stylesheet' href='prism-line-numbers.css'>
        <script src='prism.js'></script>
        <script src='prism-line-numbers.js'></script>
        <style>
            body {{ background: #1e1e1e; color: #d4d4d4; margin: 0; padding: 5px; font-size: 12px; }}
            pre {{ margin: 0; }}
        </style>
    </head>
    <body class='line-numbers'>
        <pre><code class='language-html'>{System.Web.HttpUtility.HtmlEncode(_currentHtml)}</code></pre>
    </body>
    </html>";

        // BaseUrl pour permettre à la WebView de trouver les fichiers dans Resources/Raw
        htmlSource.BaseUrl = DeviceInfo.Platform == DevicePlatform.Android
            ? "file:///android_asset/"
            : "";

        CodeWebView.Source = htmlSource;
    }

    private void OnPrettifyClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_currentHtml)) return;
        _currentHtml = Regex.Replace(_currentHtml, @">(?=<)", ">\n    ");
        LoadEditor();
    }

    private void ShowPreview(object sender, EventArgs e)
    {
        CodeWebView.IsVisible = false;
        ConsoleView.IsVisible = false;
        PreviewWebView.IsVisible = true;
        PreviewWebView.Source = new HtmlWebViewSource { Html = _currentHtml };
    }

    private async void OnReplaceClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(FindEntry.Text)) return;

        if (_currentHtml.Contains(FindEntry.Text))
        {
            _currentHtml = _currentHtml.Replace(FindEntry.Text, ReplaceEntry.Text);
            LoadEditor();
        }
        else
        {
            await DisplayAlert("Info", "Texte non trouvé", "OK");
        }
    }

    private async void OnExportClicked(object sender, EventArgs e)
    {
        try
        {
            var path = Path.Combine(FileSystem.CacheDirectory, "article_debug.html");
            await File.WriteAllTextAsync(path, _currentHtml);
            await Share.Default.RequestAsync(new ShareFileRequest { Title = "Export Source", File = new ShareFile(path) });
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erreur", ex.Message, "OK");
        }
    }

    private void ShowEditor(object sender, EventArgs e)
    {
        PreviewWebView.IsVisible = false; ConsoleView.IsVisible = false; CodeWebView.IsVisible = true;
    }

    private void ShowConsole(object sender, EventArgs e)
    {
        CodeWebView.IsVisible = false; PreviewWebView.IsVisible = false; ConsoleView.IsVisible = true;
        ConsoleLogLabel.Text += $"\n[{DateTime.Now:HH:mm:ss}] Console active...";
    }

    // CORRECTION DU BOUTON FERMER : Utilise la navigation Shell pour revenir en arrière
    private async void OnCloseClicked(object sender, EventArgs e)
    {
        try
        {
            // "//" indique au Shell de remonter à la racine de la navigation 
            // et de charger la page définie comme accueil (MainPage).
            await Shell.Current.GoToAsync("//MainPage");
        }
        catch (Exception ex)
        {

            // Si tu as un nom de route différent pour ton accueil, 
            // tu peux aussi essayer de remonter tout en haut de la pile :
            await Shell.Current.GoToAsync("///");
        }
    }
}