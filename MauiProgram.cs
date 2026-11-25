// MauiProgram.cs
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rss_feeder_prout.Services;
using Rss_feeder_prout.ViewModels;
using Rss_feeder_prout.Views;

namespace Rss_feeder_prout
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            // Journal des erreurs + interception des erreurs non gérées
            CrashLog.Install();

            // Encodages des vieux sites (windows-1252, iso-8859-15…)
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if ANDROID
            // WebView du lecteur : JavaScript + accès aux fichiers locaux (images hors-ligne)
            Microsoft.Maui.Handlers.WebViewHandler.Mapper.AppendToMapping("RssProutReader", (handler, view) =>
            {
                var settings = handler.PlatformView.Settings;
                settings.JavaScriptEnabled = true;
                settings.AllowFileAccess = true;
                settings.DomStorageEnabled = true;
                settings.LoadWithOverviewMode = true;
                settings.UseWideViewPort = true;
                settings.BuiltInZoomControls = true;
                settings.DisplayZoomControls = false;
                handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
            });
#endif

#if DEBUG
            builder.Logging.AddDebug();
#endif

            // --- Services (Singletons : base de données, RSS, synchro, planificateur...) ---
            builder.Services.AddSingleton<SQLiteService>();
            builder.Services.AddSingleton<ImageCacheService>();
            builder.Services.AddSingleton<RssService>();
            builder.Services.AddSingleton<OpmlService>();
            builder.Services.AddSingleton<SyncService>();
            builder.Services.AddSingleton<SyncScheduler>();
            builder.Services.AddSingleton<BackupService>();
            builder.Services.AddSingleton<ReaderSession>();

            // --- Onglets (Singletons : gardent leur état) ---
            builder.Services.AddSingleton<MainViewModel>();
            builder.Services.AddSingleton<MainPage>();
            builder.Services.AddSingleton<SearchViewModel>();
            builder.Services.AddSingleton<SearchPage>();
            builder.Services.AddSingleton<FavoritesViewModel>();
            builder.Services.AddSingleton<FavoritesPage>();
            builder.Services.AddSingleton<ArchiveViewModel>();
            builder.Services.AddSingleton<ArchivePage>();
            builder.Services.AddSingleton<MorePage>();

            // --- Pages ouvertes par-dessus (Transients : nouvelle instance à chaque fois) ---
            builder.Services.AddTransient<SourcesViewModel>();
            builder.Services.AddTransient<SourcesPage>();

            builder.Services.AddTransient<ArticleListViewModel>();
            builder.Services.AddTransient<ArticleListPage>();

            builder.Services.AddTransient<ArticleDetailViewModel>();
            builder.Services.AddTransient<ArticleDetailPage>();

            builder.Services.AddTransient<ItemDetailViewModel>();
            builder.Services.AddTransient<ItemDetailPage>();

            builder.Services.AddTransient<PlaylistManagerViewModel>();
            builder.Services.AddTransient<PlaylistManagerPage>();

            builder.Services.AddTransient<PlaylistDetailViewModel>();
            builder.Services.AddTransient<PlaylistDetailPage>();

            builder.Services.AddTransient<DatabaseManagerViewModel>();
            builder.Services.AddTransient<DatabaseManagerPage>();

            builder.Services.AddTransient<SettingsViewModel>();
            builder.Services.AddTransient<OptionRssPage>();

            builder.Services.AddTransient<StatsViewModel>();
            builder.Services.AddTransient<StatsPage>();

            builder.Services.AddTransient<SourceCodePage>();
            builder.Services.AddTransient<SyncOptionsPage>();

            return builder.Build();
        }
    }
}
