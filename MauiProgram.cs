// MauiProgram.cs
using Microsoft.Extensions.DependencyInjection;
using Rss_feeder_prout.Services;
using Rss_feeder_prout.ViewModels;
using Rss_feeder_prout.Views;
using Microsoft.Maui.Controls.Hosting;

namespace Rss_feeder_prout
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                    // Assurez-vous d'ajouter ici les fichiers de polices nécessaires pour Font Awesome (FAS) si ce n'est pas déjà fait !
                });

            // Enregistrement des Services (Singleton pour la BD, le RSS et l'OPML)
            builder.Services.AddSingleton<SQLiteService>();
            builder.Services.AddSingleton<RssService>();
            builder.Services.AddSingleton<OpmlService>();

            // --- Enregistrement des ViewModels et Vues ---

            // Singletons (Maintient l'état)
            builder.Services.AddSingleton<MainViewModel>();
            builder.Services.AddSingleton<MainPage>();

            // Transients (Création d'une nouvelle instance à chaque fois)
            builder.Services.AddTransient<PlaylistManagerViewModel>();
            builder.Services.AddTransient<PlaylistManagerPage>();

            builder.Services.AddTransient<PlaylistDetailViewModel>();
            builder.Services.AddTransient<PlaylistDetailPage>();

            // Si elles existent
            builder.Services.AddTransient<ItemDetailViewModel>();
            builder.Services.AddTransient<ItemDetailPage>();

            builder.Services.AddTransient<ArticleDetailViewModel>(); // Assurez-vous que ce ViewModel existe
            builder.Services.AddTransient<ArticleDetailPage>();     // Assurez-vous que cette Page existe
            // FIN DU NOUVEL ENREGISTREMENT

            // 🎯 NOUVEL ENREGISTREMENT POUR LA GESTION DE LA DB
            builder.Services.AddTransient<DatabaseManagerViewModel>();
            builder.Services.AddTransient<DatabaseManagerPage>();

            builder.Services.AddTransient<ArchivePage>();
            builder.Services.AddTransient<ArchiveViewModel>();

            // FIN DU NOUVEL ENREGISTREMENT

            return builder.Build();
        }
    }
}