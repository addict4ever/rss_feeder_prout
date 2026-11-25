using Rss_feeder_prout.Helpers;
using Rss_feeder_prout.Models;
using System.Diagnostics;
using System.Net;
using Microsoft.Maui.Graphics;
#if ANDROID || IOS || MACCATALYST
using Microsoft.Maui.Graphics.Platform;
#endif

namespace Rss_feeder_prout.Services
{
    /// <summary>
    /// Cache d'images hors-ligne.
    ///  - nom de fichier = SHA1 de l'URL (stable entre les lancements, contrairement à GetHashCode)
    ///  - mode Original : copie telle quelle
    ///  - mode Optimized : redimensionnement (largeur max) + compression JPEG (qualité réglable)
    ///  - secours : proxy d'images Google (méthode d'origine) si activé
    /// </summary>
    public class ImageCacheService
    {
        private const long MaxImageBytes = 15 * 1024 * 1024;
        private readonly HttpClient _http;
        private static readonly SemaphoreSlim _diskLock = new(1, 1);

        public ImageCacheService()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                AllowAutoRedirect = true,
                UseCookies = false
            };
            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Mobile Safari/537.36");
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "fr-FR,fr;q=0.9,en;q=0.8");
        }

        public static string GooglePrefix => "https://images1-focus-opensocial.googleusercontent.com/gadgets/proxy?container=focus&refresh=2592000&url=";

        /// <summary>Si l'URL est déjà passée par le proxy Google, retourne l'URL d'origine.</summary>
        public static string UnwrapProxy(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            int idx = url.IndexOf("gadgets/proxy", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return url;
            int u = url.IndexOf("url=", idx, StringComparison.OrdinalIgnoreCase);
            return u > 0 ? Uri.UnescapeDataString(url.Substring(u + 4)) : url;
        }

        public static string WrapProxy(string url) => GooglePrefix + Uri.EscapeDataString(url);

        public string PathFor(string url, ImageMode mode)
        {
            var hash = FormatHelper.StableHash(UnwrapProxy(url) + (mode == ImageMode.Optimized ? $"|opt{AppSettings.ImageMaxWidth}" : string.Empty));
            var ext = mode == ImageMode.Optimized ? ".jpg" : GuessExtension(url);
            return Path.Combine(SQLiteService.ImageCacheFolder, hash + ext);
        }

        /// <summary>
        /// Télécharge (si besoin) une image et retourne son chemin local, ou null en cas d'échec.
        /// </summary>
        public async Task<string?> CacheImageAsync(string? url, ImageMode mode, string? referer = null, CancellationToken ct = default)
        {
            if (mode == ImageMode.None || string.IsNullOrWhiteSpace(url)) return null;
            url = url.Trim();
            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;
            if (url.StartsWith("//")) url = "https:" + url;
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;

            var original = UnwrapProxy(url);
            var localPath = PathFor(original, mode);

            if (File.Exists(localPath) && new FileInfo(localPath).Length > 0) return localPath;

            byte[]? bytes = await DownloadAsync(original, referer, ct);

            // Secours : proxy Google (méthode anti-hotlink d'origine)
            if (bytes == null && AppSettings.UseGoogleImageProxy)
                bytes = await DownloadAsync(WrapProxy(original), null, ct);

            if (bytes == null || bytes.Length == 0) return null;

            if (mode == ImageMode.Optimized && !IsVectorOrAnimated(original, bytes))
            {
                var optimized = await Task.Run(() => Optimize(bytes, AppSettings.ImageMaxWidth, AppSettings.ImageQuality), ct);
                if (optimized != null && optimized.Length > 0 && optimized.Length < bytes.Length)
                    bytes = optimized;
                else if (optimized == null)
                    localPath = Path.ChangeExtension(localPath, GuessExtension(original)); // impossible à décoder : on garde l'original
            }

            await _diskLock.WaitAsync(ct);
            try
            {
                Directory.CreateDirectory(SQLiteService.ImageCacheFolder);
                await File.WriteAllBytesAsync(localPath, bytes, ct);
            }
            finally
            {
                _diskLock.Release();
            }

            return localPath;
        }

        private async Task<byte[]?> DownloadAsync(string url, string? referer, CancellationToken ct)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var refUri))
                    request.Headers.Referrer = refUri;

                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode) return null;

                var type = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (type.StartsWith("text/", StringComparison.OrdinalIgnoreCase)) return null; // page d'erreur HTML

                var length = response.Content.Headers.ContentLength;
                if (length.HasValue && length.Value > MaxImageBytes) return null;

                var data = await response.Content.ReadAsByteArrayAsync(ct);
                return data.Length > MaxImageBytes ? null : data;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IMAGE ERROR] {url} : {ex.Message}");
                return null;
            }
        }

        /// <summary>Redimensionne (largeur max) et compresse en JPEG. null si non décodable.</summary>
        private static byte[]? Optimize(byte[] data, int maxWidth, int quality)
        {
#if ANDROID || IOS || MACCATALYST
            try
            {
                using var input = new MemoryStream(data);
                Microsoft.Maui.Graphics.IImage? image = PlatformImage.FromStream(input);
                if (image == null) return null;

                try
                {
                    if (image.Width > maxWidth)
                    {
                        var resized = image.Downsize(maxWidth, maxWidth * 4f, true);
                        image = resized;
                    }

                    using var output = new MemoryStream();
                    image.Save(output, Microsoft.Maui.Graphics.ImageFormat.Jpeg, Math.Clamp(quality / 100f, 0.3f, 1f));
                    return output.ToArray();
                }
                finally
                {
                    image.Dispose();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IMAGE OPTIMIZE] {ex.Message}");
                return null;
            }
#else
            return null;
#endif
        }

        private static bool IsVectorOrAnimated(string url, byte[] data)
        {
            var lower = url.Split('?')[0].ToLowerInvariant();
            if (lower.EndsWith(".svg") || lower.EndsWith(".gif")) return true;
            // Signature GIF / SVG
            if (data.Length > 4 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F') return true;
            if (data.Length > 5 && (data[0] == '<' || (data[0] == 0xEF && data.Length > 8 && data[3] == '<'))) return true;
            return false;
        }

        private static string GuessExtension(string url)
        {
            try
            {
                var path = new Uri(UnwrapProxy(url)).AbsolutePath;
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".svg" or ".avif" or ".bmp")
                    return ext;
            }
            catch { }
            return ".img";
        }

        // ------------------------------------------------------------------
        // Lecteur : conversion en data URI (fonctionne dans tous les WebView, sans accès fichier)
        // ------------------------------------------------------------------

        public static async Task<string?> ToDataUriAsync(string localPath)
        {
            try
            {
                if (!File.Exists(localPath)) return null;
                var bytes = await File.ReadAllBytesAsync(localPath);
                var mime = MimeFor(localPath, bytes);
                return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
            }
            catch
            {
                return null;
            }
        }

        private static string MimeFor(string path, byte[] bytes)
        {
            if (bytes.Length > 3 && bytes[0] == 0x89 && bytes[1] == 'P') return "image/png";
            if (bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8) return "image/jpeg";
            if (bytes.Length > 3 && bytes[0] == 'G' && bytes[1] == 'I') return "image/gif";
            if (bytes.Length > 12 && bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B') return "image/webp";
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".svg" => "image/svg+xml",
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".avif" => "image/avif",
                _ => "image/jpeg"
            };
        }

        // ------------------------------------------------------------------
        // Maintenance
        // ------------------------------------------------------------------

        public async Task<(int count, long bytes)> ClearCacheAsync(int olderThanDays = 0)
        {
            return await Task.Run(() =>
            {
                int count = 0;
                long bytes = 0;
                var limit = DateTime.Now.AddDays(-olderThanDays);
                var folders = new[] { SQLiteService.ImageCacheFolder };
                foreach (var folder in folders)
                {
                    if (!Directory.Exists(folder)) continue;
                    foreach (var f in Directory.EnumerateFiles(folder).ToList())
                    {
                        try
                        {
                            var info = new FileInfo(f);
                            if (olderThanDays > 0 && info.LastWriteTime > limit) continue;
                            bytes += info.Length;
                            info.Delete();
                            count++;
                        }
                        catch { }
                    }
                }

                // Anciennes images de la v1 (racine AppData : cache_*.jpg)
                if (olderThanDays == 0)
                {
                    foreach (var f in Directory.EnumerateFiles(SQLiteService.AppDataFolder, "cache_*").ToList())
                    {
                        try { var info = new FileInfo(f); bytes += info.Length; info.Delete(); count++; } catch { }
                    }
                }
                return (count, bytes);
            });
        }
    }
}
