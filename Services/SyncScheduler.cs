using Rss_feeder_prout.Helpers;
using Rss_feeder_prout.Models;
using System.Diagnostics;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Networking;

namespace Rss_feeder_prout.Services
{
    /// <summary>
    /// Planificateur de synchronisation automatique — CORRIGÉ.
    ///
    /// Ancien problème : un DispatcherTimer redémarrait de zéro à chaque lancement, s'arrêtait quand
    /// l'app était fermée et ne rattrapait jamais une heure manquée.
    ///
    /// Maintenant :
    ///  - la prochaine échéance est CALCULÉE (intervalle depuis la dernière synchro, ou heures fixes)
    ///    et ENREGISTRÉE ;
    ///  - au lancement / retour dans l'app, une échéance dépassée est rattrapée immédiatement ;
    ///  - un minuteur vérifie l'échéance chaque minute tant que l'app tourne ;
    ///  - sur Android, une tâche JobScheduler est programmée pour l'échéance : la synchro se fait
    ///    même app fermée, et la tâche survit au redémarrage du téléphone.
    /// </summary>
    public class SyncScheduler
    {
        private readonly SyncService _sync;
        private IDispatcherTimer? _timer;
        private bool _checking;

        public event EventHandler? ScheduleChanged;

        public SyncScheduler(SyncService sync)
        {
            _sync = sync;
        }

        public DateTime? NextRunUtc
        {
            get
            {
                var v = AppSettings.NextSyncUtc;
                return v == DateTime.MinValue || !AppSettings.AutoSyncEnabled ? null : DateTime.SpecifyKind(v, DateTimeKind.Utc);
            }
        }

        public string NextRunText
        {
            get
            {
                if (!AppSettings.AutoSyncEnabled) return "Synchro automatique désactivée";
                var next = NextRunUtc;
                if (next == null) return "Prochaine synchro : non planifiée";
                var local = next.Value.ToLocalTime();
                return local <= DateTime.Now.AddMinutes(1) ? "Prochaine synchro : imminente" : "Prochaine synchro " + DateHelper.ToFuture(local);
            }
        }

        // ------------------------------------------------------------------
        // Démarrage
        // ------------------------------------------------------------------

        /// <summary>Appelé au démarrage : minuteur + planification + rattrapage.</summary>
        public void Start(IDispatcher dispatcher)
        {
            if (_timer == null)
            {
                _timer = dispatcher.CreateTimer();
                _timer.Interval = TimeSpan.FromMinutes(1);
                _timer.IsRepeating = true;
                _timer.Tick += async (s, e) => await CheckDueAsync();
                _timer.Start();
            }

            Reschedule();
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(8)); // laisse l'interface démarrer
                await CheckDueAsync();
            });
        }

        // ------------------------------------------------------------------
        // Calcul de l'échéance
        // ------------------------------------------------------------------

        public static DateTime? ComputeNextRunUtc(DateTime nowUtc)
        {
            if (!AppSettings.AutoSyncEnabled) return null;

            if (AppSettings.ScheduleMode == 1)
            {
                var times = AppSettings.GetFixedTimes();
                if (times.Count == 0) return null;

                var lastAuto = AppSettings.LastAutoSyncUtc;
                var lastLocal = lastAuto == DateTime.MinValue
                    ? DateTime.Now.AddMinutes(-1)
                    : DateTime.SpecifyKind(lastAuto, DateTimeKind.Utc).ToLocalTime();

                // Première heure fixe STRICTEMENT après la dernière synchro auto.
                // Si elle est déjà passée -> due maintenant (rattrapage d'un créneau manqué).
                for (int day = 0; day <= 2; day++)
                {
                    var date = lastLocal.Date.AddDays(day);
                    foreach (var t in times)
                    {
                        var candidate = date + t;
                        if (candidate > lastLocal)
                        {
                            var candidateUtc = candidate.ToUniversalTime();
                            return candidateUtc < nowUtc ? nowUtc : candidateUtc;
                        }
                    }
                }
                return nowUtc;
            }
            else
            {
                var interval = TimeSpan.FromMinutes(Math.Max(5, AppSettings.IntervalMinutes));
                var last = AppSettings.LastSyncUtc;
                if (last == DateTime.MinValue) return nowUtc.AddMinutes(2);

                var next = DateTime.SpecifyKind(last, DateTimeKind.Utc) + interval;
                return next < nowUtc ? nowUtc : next;
            }
        }

        /// <summary>Recalcule et enregistre l'échéance, puis (Android) programme la tâche système.</summary>
        public void Reschedule()
        {
            try
            {
                var next = ComputeNextRunUtc(DateTime.UtcNow);
                AppSettings.NextSyncUtc = next ?? DateTime.MinValue;

#if ANDROID
                if (next.HasValue)
                    Rss_feeder_prout.Droid.AndroidSyncJobScheduler.Schedule(next.Value, AppSettings.WifiOnly, AppSettings.ChargingOnly);
                else
                    Rss_feeder_prout.Droid.AndroidSyncJobScheduler.Cancel();
#endif
                Debug.WriteLine($"[SCHEDULER] Prochaine synchro : {(next.HasValue ? next.Value.ToLocalTime().ToString("g") : "aucune")}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SCHEDULER] {ex.Message}");
            }

            MainThread.BeginInvokeOnMainThread(() => ScheduleChanged?.Invoke(this, EventArgs.Empty));
        }

        // ------------------------------------------------------------------
        // Exécution
        // ------------------------------------------------------------------

        /// <summary>Vérifie si l'échéance est atteinte (appelé chaque minute + au retour dans l'app).</summary>
        public async Task CheckDueAsync()
        {
            if (_checking || !AppSettings.AutoSyncEnabled || _sync.IsRunning) return;

            var next = NextRunUtc;
            if (next == null)
            {
                Reschedule();
                return;
            }

            if (DateTime.UtcNow < next.Value) return;

            _checking = true;
            try
            {
                await RunScheduledSyncAsync(SyncTrigger.Auto, CancellationToken.None);
            }
            finally
            {
                _checking = false;
            }
        }

        /// <summary>Synchro planifiée (appelée par le minuteur OU par la tâche Android en arrière-plan).</summary>
        public async Task<SyncResult?> RunScheduledSyncAsync(SyncTrigger trigger, CancellationToken ct)
        {
            if (!AppSettings.AutoSyncEnabled) return null;

            if (!ConditionsMet(out var reason))
            {
                Debug.WriteLine($"[SCHEDULER] Reporté : {reason}");
                // On réessaie dans 15 minutes sans perdre le créneau
                AppSettings.NextSyncUtc = DateTime.UtcNow.AddMinutes(15);
#if ANDROID
                Rss_feeder_prout.Droid.AndroidSyncJobScheduler.Schedule(AppSettings.NextSyncUtc, AppSettings.WifiOnly, AppSettings.ChargingOnly);
#endif
                return null;
            }

            var result = await _sync.RunAsync(new SyncRequest
            {
                Trigger = trigger,
                DownloadContent = AppSettings.AutoDownloadContent,
                ImageMode = AppSettings.AutoImageMode,
                MaxArticles = AppSettings.MaxArticlesPerSync,
                Label = "Synchro automatique"
            }, ct);

            if (!result.Skipped)
            {
                if (AppSettings.NotifyOnSync && (result.NewItems > 0 || result.DownloadedItems > 0))
                    NotificationService.ShowSyncCompleted(result);
            }

            Reschedule();
            return result;
        }

        private static bool ConditionsMet(out string reason)
        {
            reason = string.Empty;
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                reason = "pas d'Internet";
                return false;
            }

            if (AppSettings.WifiOnly && !Connectivity.Current.ConnectionProfiles.Contains(ConnectionProfile.WiFi)
                && !Connectivity.Current.ConnectionProfiles.Contains(ConnectionProfile.Ethernet))
            {
                reason = "Wi-Fi requis";
                return false;
            }

            if (AppSettings.ChargingOnly)
            {
                try
                {
                    var state = Battery.Default.State;
                    if (state != BatteryState.Charging && state != BatteryState.Full)
                    {
                        reason = "en attente de la recharge";
                        return false;
                    }
                }
                catch { /* information indisponible : on continue */ }
            }

            return true;
        }
    }
}
