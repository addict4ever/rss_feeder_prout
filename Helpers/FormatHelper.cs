using System.Security.Cryptography;
using System.Text;

namespace Rss_feeder_prout.Helpers
{
    public static class FormatHelper
    {
        public static string Bytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} o";
            double kb = bytes / 1024.0;
            if (kb < 1024) return $"{kb:0} Ko";
            double mb = kb / 1024.0;
            if (mb < 1024) return $"{mb:0.0} Mo";
            return $"{mb / 1024.0:0.00} Go";
        }

        /// <summary>
        /// Hash STABLE d'une chaîne (remplace string.GetHashCode qui change à chaque lancement
        /// en .NET et rendait le cache d'images inutilisable).
        /// </summary>
        public static string StableHash(string input)
        {
            var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(input ?? string.Empty));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        public static string SafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "fichier";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
                sb.Append(invalid.Contains(c) || c == ' ' ? '_' : c);
            var s = sb.ToString();
            return s.Length > 60 ? s.Substring(0, 60) : s;
        }

        public static long DirectorySize(string folder, out int fileCount, string pattern = "*")
        {
            fileCount = 0;
            if (!Directory.Exists(folder)) return 0;
            long total = 0;
            foreach (var f in Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; fileCount++; } catch { }
            }
            return total;
        }

        public static long FileSizeWithWal(string dbPath)
        {
            long size = 0;
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            {
                try { if (File.Exists(p)) size += new FileInfo(p).Length; } catch { }
            }
            return size;
        }

        public static string Plural(int count, string singular, string plural) => count == 1 ? $"1 {singular}" : $"{count} {plural}";
    }
}
