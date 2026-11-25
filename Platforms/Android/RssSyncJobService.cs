using System.Diagnostics;
using Android.App;
using Android.App.Job;
using Microsoft.Extensions.DependencyInjection;
using Rss_feeder_prout.Models;
using Rss_feeder_prout.Services;

namespace Rss_feeder_prout.Droid
{
    /// <summary>
    /// Tâche Android exécutée par le système à l'heure prévue, même si l'application est fermée.
    /// Elle lance la synchro automatique puis reprogramme la suivante.
    /// </summary>
    [Service(Name = "com.companyname.rss_feeder_prout.RssSyncJobService",
             Permission = "android.permission.BIND_JOB_SERVICE",
             Exported = true)]
    public class RssSyncJobService : JobService
    {
        private CancellationTokenSource? _cts;

        public override bool OnStartJob(JobParameters? @params)
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Task.Run(async () =>
            {
                try
                {
                    var services = IPlatformApplication.Current?.Services;
                    var scheduler = services?.GetService<SyncScheduler>();
                    if (scheduler != null)
                    {
                        Debug.WriteLine("[JOB] Démarrage de la synchro en arrière-plan.");
                        await scheduler.RunScheduledSyncAsync(SyncTrigger.Background, token);
                    }
                    else
                    {
                        Debug.WriteLine("[JOB] Services MAUI indisponibles.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[JOB ERROR] {ex.Message}");
                }
                finally
                {
                    try { JobFinished(@params, false); } catch { }
                }
            });

            return true; // travail asynchrone en cours
        }

        public override bool OnStopJob(JobParameters? @params)
        {
            // Le système interrompt la tâche (réseau perdu, limite de temps…) : on annule et on demande une reprise
            try { _cts?.Cancel(); } catch { }
            return true;
        }
    }
}
