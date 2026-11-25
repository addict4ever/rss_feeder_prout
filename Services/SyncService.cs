using Rss_feeder_prout.Models;
using System.Diagnostics;
using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Networking;

namespace Rss_feeder_prout.Services
{
    /// <summary>
    /// Orchestrateur UNIQUE de synchronisation (manuelle, automatique, arrière-plan).
    /// - une seule synchro à la fois
    /// - NON BLOQUANT : l'utilisateur continue à lire pendant la synchro (bandeau de progression)
    /// - journal des synchros, rétention automatique, mise à jour des icônes
    /// </summary>
    public class SyncService : ObservableModel
    {
        private readonly SQLiteService _db;
        private readonly RssService _rss;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private CancellationTokenSource? _cts;

        public event EventHandler<SyncResult>? SyncCompleted;
        public event EventHandler? SyncStarted;

        public SyncService(SQLiteService db, RssService rss)
        {
            _db = db;
            _rss = rss;
            CancelCommand = new Command(Cancel);
        }

        // ------------------------------------------------------------------
        // État observable (bandeau de synchro)
        // ------------------------------------------------------------------

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (SetProperty(ref _isRunning, value))
                    OnPropertyChanged(nameof(IsIdle));
            }
        }

        public bool IsIdle => !IsRunning;

        private double _progress;
        public double Progress
        {
            get => _progress;
            private set => SetProperty(ref _progress, value);
        }

        private string _statusTitle = string.Empty;
        public string StatusTitle
        {
            get => _statusTitle;
            private set => SetProperty(ref _statusTitle, value);
        }

        private string _statusText = string.Empty;
        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        private SyncResult? _lastResult;
        public SyncResult? LastResult
        {
            get => _lastResult;
            private set => SetProperty(ref _lastResult, value);
        }

        public ICommand CancelCommand { get; }

        public void Cancel()
        {
            try { _cts?.Cancel(); } catch { }
            UpdateUi(status: "Arrêt en cours…");
        }

        private void UpdateUi(double? progress = null, string? title = null, string? status = null, bool? running = null)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (running.HasValue) IsRunning = running.Value;
                if (progress.HasValue) Progress = Math.Clamp(progress.Value, 0, 1);
                if (title != null) StatusTitle = title;
                if (status != null) StatusText = status;
            });
        }

        // ------------------------------------------------------------------
        // Synchronisation
        // ------------------------------------------------------------------

        public async Task<SyncResult> RunAsync(SyncRequest request, CancellationToken externalToken = default)
        {
            var result = new SyncResult();

            if (!await _gate.WaitAsync(0))
            {
                result.Skipped = true;
                result.Message = "Une synchronisation est déjà en cours.";
                return result;
            }

            var started = DateTime.UtcNow;
            var sw = Stopwatch.StartNew();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            var ct = _cts.Token;

            try
            {
                if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                {
                    result.Skipped = true;
                    result.Message = "Pas de connexion Internet.";
                    return result;
                }

                UpdateUi(0, request.Label, "Préparation…", true);
                MainThread.BeginInvokeOnMainThread(() => SyncStarted?.Invoke(this, EventArgs.Empty));

                // 1. Catégories & sources concernées
                var playlists = await _db.GetPlaylistsAsync();
                if (request.PlaylistIds != null && request.PlaylistIds.Count > 0)
                    playlists = playlists.Where(p => request.PlaylistIds.Contains(p.Id)).ToList();
                else if (request.OnlyActivePlaylists)
                    playlists = playlists.Where(p => p.IsActive).ToList();

                var playlistIds = playlists.Select(p => p.Id).ToHashSet();
                var sites = (await _db.GetSitesAsync())
                    .Where(s => playlistIds.Contains(s.PlaylistId) && !s.IsDisabled)
                    .ToList();

                result.TotalFeeds = sites.Count;
                if (sites.Count == 0)
                {
                    result.Message = "Aucune source active à synchroniser.";
                    result.Completed = true;
                    return result;
                }

                double feedWeight = request.DownloadContent ? 0.35 : 0.95;

                // 2. Flux (en parallèle)
                int done = 0;
                int newTotal = 0;
                int failed = 0;
                using (var gate = new SemaphoreSlim(6))
                {
                    var tasks = sites.Select(async site =>
                    {
                        await gate.WaitAsync(ct);
                        try
                        {
                            var (n, ok) = await _rss.UpdateSiteFeedAsync(site, request.ImageMode, ct);
                            Interlocked.Add(ref newTotal, n);
                            if (!ok)
                            {
                                Interlocked.Increment(ref failed);
                                lock (result.Errors) result.Errors.Add($"{site.DisplayName} : {site.LastError}");
                            }
                        }
                        catch (OperationCanceledException) { }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref failed);
                            lock (result.Errors) result.Errors.Add($"{site.DisplayName} : {ex.Message}");
                        }
                        finally
                        {
                            gate.Release();
                            int d = Interlocked.Increment(ref done);
                            UpdateUi(feedWeight * d / sites.Count, null, $"Flux {d}/{sites.Count} · {site.DisplayName} · {Volatile.Read(ref newTotal)} nouveaux");
                        }
                    }).ToList();

                    await Task.WhenAll(tasks);
                }

                result.NewItems = newTotal;
                result.FailedFeeds = failed;
                ct.ThrowIfCancellationRequested();

                // 3. Icônes manquantes (rapide, en tâche de fond)
                _ = Task.Run(async () =>
                {
                    foreach (var s in sites.Where(s => string.IsNullOrEmpty(s.LocalIconPath) || !File.Exists(s.LocalIconPath)).Take(40))
                    {
                        try { await _rss.EnsureSiteIconAsync(s); } catch { }
                    }
                });

                // 4. Contenu complet (hors-ligne)
                var fullSites = sites.Where(s => s.AlwaysDownloadFull).Select(s => s.Id).ToHashSet();
                if (request.DownloadContent || fullSites.Count > 0)
                {
                    var candidates = await _db.GetItemsToDownloadAsync(playlistIds, request.MaxArticles);
                    if (!request.DownloadContent)
                        candidates = candidates.Where(i => i.SiteId.HasValue && fullSites.Contains(i.SiteId.Value)).ToList();

                    int dDone = 0;
                    int dOk = 0;
                    int dTotal = candidates.Count;
                    if (dTotal > 0)
                    {
                        using var gate = new SemaphoreSlim(3);
                        var tasks = candidates.Select(async item =>
                        {
                            await gate.WaitAsync(ct);
                            try
                            {
                                if (await _rss.DownloadAndSaveItemContentAsync(item, request.ImageMode, ct))
                                    Interlocked.Increment(ref dOk);
                            }
                            catch (OperationCanceledException) { }
                            catch (Exception ex) { Debug.WriteLine($"[SYNC CONTENT] {ex.Message}"); }
                            finally
                            {
                                gate.Release();
                                int d = Interlocked.Increment(ref dDone);
                                UpdateUi(feedWeight + (1 - feedWeight) * d / dTotal, null,
                                    $"Hors-ligne {d}/{dTotal} · {Truncate(item.Title, 40)}");
                            }
                        }).ToList();
                        await Task.WhenAll(tasks);
                    }
                    result.DownloadedItems = dOk;
                }

                ct.ThrowIfCancellationRequested();

                // 5. Rétention automatique
                UpdateUi(0.99, null, "Nettoyage…");
                result.CleanedItems = await _db.ApplyRetentionAsync();

                result.Completed = true;
            }
            catch (OperationCanceledException)
            {
                result.Cancelled = true;
            }
            catch (Exception ex)
            {
                result.Message = ex.Message;
                result.Errors.Add(ex.Message);
                Debug.WriteLine($"[SYNC ERROR] {ex}");
            }
            finally
            {
                sw.Stop();
                result.Duration = sw.Elapsed;

                if (!result.Skipped)
                {
                    AppSettings.LastSyncUtc = DateTime.UtcNow;
                    if (request.Trigger is SyncTrigger.Auto or SyncTrigger.Background)
                        AppSettings.LastAutoSyncUtc = DateTime.UtcNow;

                    try
                    {
                        await _db.AddSyncLogAsync(new SyncLogEntry
                        {
                            StartedUtc = started,
                            DurationSeconds = sw.Elapsed.TotalSeconds,
                            Trigger = request.Trigger.ToString(),
                            TotalFeeds = result.TotalFeeds,
                            FailedFeeds = result.FailedFeeds,
                            NewItems = result.NewItems,
                            DownloadedItems = result.DownloadedItems,
                            Cancelled = result.Cancelled,
                            Message = string.IsNullOrEmpty(result.Message) ? string.Join(" | ", result.Errors.Take(5)) : result.Message
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SYNC LOG] {ex.Message}");
                    }
                }

                _cts?.Dispose();
                _cts = null;
                _gate.Release();

                UpdateUi(1, null, result.Skipped ? result.Message : result.Summary, false);
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    LastResult = result;
                    SyncCompleted?.Invoke(this, result);
                });
            }

            return result;
        }

        private static string Truncate(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
