using Rss_feeder_prout.Services;
using System.Diagnostics;

namespace Rss_feeder_prout
{
    public partial class App : Application
    {
        private readonly SyncScheduler _scheduler;
        private readonly BackupService _backup;
        private readonly SQLiteService _db;

        public App(SyncScheduler scheduler, BackupService backup, SQLiteService db)
        {
            InitializeComponent();

            _scheduler = scheduler;
            _backup = backup;
            _db = db;

            // Thème (sombre / clair / système) + marges d'en-tête
            ThemeService.ApplyInsets();
            ThemeService.Apply();

            RequestedThemeChanged += (s, e) =>
            {
                if (AppSettings.AppTheme == "system") ThemeService.Apply("system");
            };

            // Initialisation de la base (et migration de l'ancienne organisation) dès le démarrage
            _ = Task.Run(async () =>
            {
                try
                {
                    await _db.EnsureInitialized();
                    await _backup.RunAutoBackupIfDueAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[STARTUP] {ex.Message}");
                }
            });
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(new AppShell());

            window.Created += (s, e) =>
            {
                ThemeService.ApplyStatusBar();
                // Planificateur : minuteur + programmation Android + rattrapage d'une échéance manquée
                _scheduler.Start(Dispatcher);
            };

            // Retour dans l'application : on vérifie si une synchro était due
            window.Resumed += (s, e) =>
            {
                ThemeService.ApplyStatusBar();
                _ = _scheduler.CheckDueAsync();
            };

            window.Activated += (s, e) => ThemeService.ApplyStatusBar();

            return window;
        }

        /// <summary>
        /// Conservé pour compatibilité (anciennes Options) : relance la planification.
        /// </summary>
        public void ResetTimer()
        {
            _scheduler.Reschedule();
        }
    }
}
