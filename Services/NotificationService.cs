using Rss_feeder_prout.Models;
using System.Diagnostics;
using Microsoft.Maui.ApplicationModel;

namespace Rss_feeder_prout.Services
{
    /// <summary>Notifications locales (fin de synchro automatique).</summary>
    public static class NotificationService
    {
        private const string ChannelId = "rss_sync";
        private const int NotificationId = 4208;

        /// <summary>Demande la permission (Android 13+) si nécessaire.</summary>
        public static async Task<bool> EnsurePermissionAsync()
        {
            try
            {
                var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
                if (status == PermissionStatus.Granted) return true;
                status = await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<Permissions.PostNotifications>());
                return status == PermissionStatus.Granted;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NOTIF] Permission : {ex.Message}");
                return false;
            }
        }

        public static void ShowSyncCompleted(SyncResult result)
        {
            var title = result.NewItems == 1 ? "1 nouvel article" : $"{result.NewItems} nouveaux articles";
            var text = result.DownloadedItems > 0
                ? $"{result.DownloadedItems} disponibles hors-ligne · {result.Duration.TotalSeconds:0} s"
                : "Synchronisation automatique terminée";
            Show(title, text);
        }

        public static void Show(string title, string text)
        {
#if ANDROID
            try
            {
                var context = global::Android.App.Application.Context;

                if (OperatingSystem.IsAndroidVersionAtLeast(26))
                {
                    var manager = (global::Android.App.NotificationManager?)context.GetSystemService(global::Android.Content.Context.NotificationService);
                    if (manager != null && manager.GetNotificationChannel(ChannelId) == null)
                    {
                        var channel = new global::Android.App.NotificationChannel(ChannelId, "Synchronisation", global::Android.App.NotificationImportance.Low)
                        {
                            Description = "Résultat des synchronisations automatiques"
                        };
                        manager.CreateNotificationChannel(channel);
                    }
                }

                var intent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName ?? string.Empty);
                global::Android.App.PendingIntent? pending = null;
                if (intent != null)
                {
                    intent.SetFlags(global::Android.Content.ActivityFlags.SingleTop | global::Android.Content.ActivityFlags.ClearTop);
                    pending = global::Android.App.PendingIntent.GetActivity(context, 0, intent,
                        global::Android.App.PendingIntentFlags.Immutable | global::Android.App.PendingIntentFlags.UpdateCurrent);
                }

                var builder = new AndroidX.Core.App.NotificationCompat.Builder(context, ChannelId)
                    .SetSmallIcon(global::Android.Resource.Drawable.StatSysDownloadDone)
                    .SetContentTitle(title)
                    .SetContentText(text)
                    .SetAutoCancel(true)
                    .SetPriority(AndroidX.Core.App.NotificationCompat.PriorityLow);

                if (pending != null) builder.SetContentIntent(pending);

                AndroidX.Core.App.NotificationManagerCompat.From(context).Notify(NotificationId, builder.Build());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NOTIF] {ex.Message}");
            }
#else
            Debug.WriteLine($"[NOTIF] {title} — {text}");
#endif
        }
    }
}
