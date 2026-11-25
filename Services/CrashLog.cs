using System.Diagnostics;
using System.Text;
using Microsoft.Maui.Storage;

namespace Rss_feeder_prout.Services
{
    /// <summary>
    /// Journal des erreurs (AppData/crash.log). Sert à comprendre un plantage :
    /// Plus → Journal des erreurs → Partager.
    /// </summary>
    public static class CrashLog
    {
        private static readonly object Lock = new();
        private const long MaxBytes = 512 * 1024;

        public static string FilePath => Path.Combine(FileSystem.AppDataDirectory, "crash.log");

        public static void Write(string context, Exception? ex)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ").AppendLine(context);
                var e = ex;
                int depth = 0;
                while (e != null && depth < 4)
                {
                    sb.Append("  ").Append(e.GetType().FullName).Append(": ").AppendLine(e.Message);
                    if (!string.IsNullOrEmpty(e.StackTrace))
                        sb.AppendLine(string.Join(Environment.NewLine, e.StackTrace.Split('\n').Take(12).Select(l => "    " + l.Trim())));
                    e = e.InnerException;
                    depth++;
                }
                sb.AppendLine();

                lock (Lock)
                {
                    if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                    {
                        // On garde la seconde moitié du fichier
                        var text = File.ReadAllText(FilePath);
                        File.WriteAllText(FilePath, text.Substring(text.Length / 2));
                    }
                    File.AppendAllText(FilePath, sb.ToString());
                }
                Debug.WriteLine($"[CRASHLOG] {context}: {ex?.Message}");
            }
            catch { }
        }

        public static string Read()
        {
            try { return File.Exists(FilePath) ? File.ReadAllText(FilePath) : string.Empty; }
            catch { return string.Empty; }
        }

        public static void Clear()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
        }

        /// <summary>Intercepte les erreurs non gérées (au lieu de fermer l'app quand c'est possible).</summary>
        public static void Install()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Write("Non gérée (AppDomain)", e.ExceptionObject as Exception);

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Write("Tâche non observée", e.Exception);
                e.SetObserved();
            };

#if ANDROID
            // Erreurs managées sur le thread Android : on les journalise et on évite la fermeture de l'app
            Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (s, e) =>
            {
                Write("Non gérée (Android)", e.Exception);
                e.Handled = true;
            };
#endif
        }
    }
}
