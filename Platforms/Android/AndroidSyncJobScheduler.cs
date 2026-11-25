using System.Diagnostics;
using Android.App.Job;
using Android.Content;
using Android.Runtime;

namespace Rss_feeder_prout.Droid
{
    /// <summary>
    /// Programme la synchro en arrière-plan avec le JobScheduler d'Android (intégré au système,
    /// aucun paquet supplémentaire). La tâche est persistante : elle survit au redémarrage.
    /// </summary>
    public static class AndroidSyncJobScheduler
    {
        public const int JobId = 4207;

        public static void Schedule(DateTime runAtUtc, bool wifiOnly, bool chargingOnly)
        {
            try
            {
                var context = global::Android.App.Application.Context;
                var scheduler = context.GetSystemService(Context.JobSchedulerService).JavaCast<JobScheduler>();
                if (scheduler == null) return;

                long delayMs = (long)Math.Max(0, (runAtUtc - DateTime.UtcNow).TotalMilliseconds);
                // Le système peut décaler un peu l'exécution : on lui laisse 15 minutes de marge
                long deadlineMs = delayMs + (long)TimeSpan.FromMinutes(15).TotalMilliseconds;

                var component = new ComponentName(context, Java.Lang.Class.FromType(typeof(RssSyncJobService)));
                var builder = new JobInfo.Builder(JobId, component)
                    .SetMinimumLatency(delayMs)
                    .SetOverrideDeadline(deadlineMs)
                    .SetRequiredNetworkType(wifiOnly ? NetworkType.Unmetered : NetworkType.Any)
                    .SetRequiresCharging(chargingOnly)
                    .SetPersisted(true);

                var info = builder.Build();
                int result = scheduler.Schedule(info);
                Debug.WriteLine($"[JOB] Synchro arrière-plan programmée dans {delayMs / 60000} min (résultat {result}).");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[JOB] Programmation impossible : {ex.Message}");
            }
        }

        public static void Cancel()
        {
            try
            {
                var context = global::Android.App.Application.Context;
                var scheduler = context.GetSystemService(Context.JobSchedulerService).JavaCast<JobScheduler>();
                scheduler?.Cancel(JobId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[JOB] Annulation impossible : {ex.Message}");
            }
        }
    }
}
