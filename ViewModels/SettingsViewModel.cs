using System.Collections.ObjectModel;
using System.Windows.Input;
using Rss_feeder_prout.Models;
using Rss_feeder_prout.Services;

namespace Rss_feeder_prout.ViewModels
{
    /// <summary>Réglages : synchro automatique (intervalle OU heures fixes), images, lecture, apparence, rétention, filtres.</summary>
    public class SettingsViewModel : BaseViewModel
    {
        private readonly SyncScheduler _scheduler;

        public SettingsViewModel(SyncScheduler scheduler)
        {
            _scheduler = scheduler;
            Title = "Réglages";

            foreach (var t in AppSettings.GetFixedTimes()) FixedTimes.Add(Format(t));

            AddTimeCommand = new Command(AddTime);
            RemoveTimeCommand = new Command<string>(RemoveTime);
            SyncNowCommand = new Command(async () =>
            {
                UiHelper.Toast("Synchronisation automatique lancée");
                await _scheduler.RunScheduledSyncAsync(SyncTrigger.Auto, CancellationToken.None);
                RefreshNext();
            });

            _scheduler.ScheduleChanged += (s, e) => RefreshNext();
            RefreshNext();
        }

        private void Changed()
        {
            _scheduler.Reschedule();
            RefreshNext();
        }

        private string _nextText = string.Empty;
        public string NextText
        {
            get => _nextText;
            set => SetProperty(ref _nextText, value);
        }

        private void RefreshNext()
        {
            NextText = _scheduler.NextRunText;
            var last = AppSettings.LastAutoSyncUtc;
            LastAutoText = last == DateTime.MinValue
                ? "Aucune synchro automatique pour l'instant"
                : "Dernière synchro auto : " + Helpers.DateHelper.ToRelative(DateTime.SpecifyKind(last, DateTimeKind.Utc).ToLocalTime());
        }

        private string _lastAutoText = string.Empty;
        public string LastAutoText
        {
            get => _lastAutoText;
            set => SetProperty(ref _lastAutoText, value);
        }

        // =================================================================
        // Synchro automatique
        // =================================================================

        public bool AutoSyncEnabled
        {
            get => AppSettings.AutoSyncEnabled;
            set { AppSettings.AutoSyncEnabled = value; OnPropertyChanged(); Changed(); }
        }

        public bool IsIntervalMode
        {
            get => AppSettings.ScheduleMode == 0;
            set
            {
                if (!value) return;
                AppSettings.ScheduleMode = 0;
                Notify(nameof(IsIntervalMode), nameof(IsFixedMode));
                Changed();
            }
        }

        public bool IsFixedMode
        {
            get => AppSettings.ScheduleMode == 1;
            set
            {
                if (!value) return;
                AppSettings.ScheduleMode = 1;
                Notify(nameof(IsIntervalMode), nameof(IsFixedMode));
                Changed();
            }
        }

        public static readonly int[] IntervalValues = { 5, 15, 30, 60, 120, 180, 240, 360, 480, 720, 1440 };

        public List<string> IntervalChoices { get; } = new()
        {
            "5 minutes (app ouverte)", "15 minutes", "30 minutes", "1 heure", "2 heures", "3 heures",
            "4 heures", "6 heures", "8 heures", "12 heures", "24 heures"
        };

        public int IntervalIndex
        {
            get
            {
                var v = AppSettings.IntervalMinutes;
                int best = 0;
                for (int i = 0; i < IntervalValues.Length; i++)
                    if (Math.Abs(IntervalValues[i] - v) < Math.Abs(IntervalValues[best] - v)) best = i;
                return best;
            }
            set
            {
                if (value < 0 || value >= IntervalValues.Length) return;
                AppSettings.IntervalMinutes = IntervalValues[value];
                OnPropertyChanged();
                Changed();
            }
        }

        public ObservableCollection<string> FixedTimes { get; } = new();

        private static string Format(TimeSpan t) => $"{t.Hours:00}:{t.Minutes:00}";

        private void SaveTimes()
        {
            var list = new List<TimeSpan>();
            foreach (var s in FixedTimes)
                if (TimeSpan.TryParse(s, out var ts)) list.Add(ts);
            AppSettings.SetFixedTimes(list);
        }

        private TimeSpan _newTime = new(7, 0, 0);
        public TimeSpan NewTime
        {
            get => _newTime;
            set => SetProperty(ref _newTime, value);
        }

        public ICommand AddTimeCommand { get; }
        public ICommand RemoveTimeCommand { get; }
        public ICommand SyncNowCommand { get; }

        private void AddTime()
        {
            var t = Format(new TimeSpan(NewTime.Hours, NewTime.Minutes, 0));
            if (FixedTimes.Contains(t)) { UiHelper.Toast("Cette heure existe déjà"); return; }
            var list = FixedTimes.Append(t).OrderBy(x => x).ToList();
            FixedTimes.Clear();
            foreach (var x in list) FixedTimes.Add(x);
            SaveTimes();
            Changed();
        }

        private void RemoveTime(string? t)
        {
            if (t == null) return;
            FixedTimes.Remove(t);
            SaveTimes();
            Changed();
        }

        public bool WifiOnly
        {
            get => AppSettings.WifiOnly;
            set { AppSettings.WifiOnly = value; OnPropertyChanged(); Changed(); }
        }

        public bool ChargingOnly
        {
            get => AppSettings.ChargingOnly;
            set { AppSettings.ChargingOnly = value; OnPropertyChanged(); Changed(); }
        }

        public bool AutoDownloadContent
        {
            get => AppSettings.AutoDownloadContent;
            set { AppSettings.AutoDownloadContent = value; OnPropertyChanged(); }
        }

        public List<string> ImageModeChoices { get; } = new()
        {
            "Pas d'images (texte seulement)", "Images originales", "Images optimisées (redimensionnées + compressées)"
        };

        public int AutoImageModeIndex
        {
            get => (int)AppSettings.AutoImageMode;
            set { if (value >= 0) { AppSettings.AutoImageMode = (ImageMode)value; OnPropertyChanged(); } }
        }

        public bool NotifyOnSync
        {
            get => AppSettings.NotifyOnSync;
            set
            {
                AppSettings.NotifyOnSync = value;
                OnPropertyChanged();
                if (value) _ = NotificationService.EnsurePermissionAsync();
            }
        }

        // =================================================================
        // Synchro manuelle & images
        // =================================================================

        public bool ManualDownloadContent
        {
            get => AppSettings.ManualDownloadContent;
            set { AppSettings.ManualDownloadContent = value; OnPropertyChanged(); }
        }

        public int ManualImageModeIndex
        {
            get => (int)AppSettings.ManualImageMode;
            set { if (value >= 0) { AppSettings.ManualImageMode = (ImageMode)value; OnPropertyChanged(); } }
        }

        public bool RememberSyncChoice
        {
            get => AppSettings.RememberSyncChoice;
            set { AppSettings.RememberSyncChoice = value; OnPropertyChanged(); }
        }

        public double MaxArticles
        {
            get => AppSettings.MaxArticlesPerSync;
            set { AppSettings.MaxArticlesPerSync = (int)Math.Round(value / 10) * 10; OnPropertyChanged(); OnPropertyChanged(nameof(MaxArticlesText)); }
        }

        public string MaxArticlesText => $"{AppSettings.MaxArticlesPerSync} articles max. par synchro";

        public double ImageMaxWidth
        {
            get => AppSettings.ImageMaxWidth;
            set { AppSettings.ImageMaxWidth = (int)Math.Round(value / 40) * 40; OnPropertyChanged(); OnPropertyChanged(nameof(ImageMaxWidthText)); }
        }

        public string ImageMaxWidthText => $"Largeur max. : {AppSettings.ImageMaxWidth} px";

        public double ImageQuality
        {
            get => AppSettings.ImageQuality;
            set { AppSettings.ImageQuality = (int)Math.Round(value); OnPropertyChanged(); OnPropertyChanged(nameof(ImageQualityText)); }
        }

        public string ImageQualityText => $"Qualité JPEG : {AppSettings.ImageQuality} %";

        public bool UsePaywallMethods
        {
            get => AppSettings.UsePaywallMethods;
            set { AppSettings.UsePaywallMethods = value; OnPropertyChanged(); OnPropertyChanged(nameof(UseGoogleImageProxy)); }
        }

        public bool UseGoogleImageProxy
        {
            get => AppSettings.UseGoogleImageProxy;
            set { AppSettings.UseGoogleImageProxy = value; OnPropertyChanged(); }
        }

        public bool DataSaver
        {
            get => AppSettings.DataSaver;
            set { AppSettings.DataSaver = value; OnPropertyChanged(); }
        }

        // =================================================================
        // Apparence & lecture
        // =================================================================

        public List<string> ThemeChoices { get; } = new() { "Sombre", "Clair", "Comme le système" };

        public int ThemeIndex
        {
            get => AppSettings.AppTheme switch { "light" => 1, "system" => 2, _ => 0 };
            set
            {
                var mode = value switch { 1 => "light", 2 => "system", _ => "dark" };
                if (mode == AppSettings.AppTheme) return;
                AppSettings.AppTheme = mode;
                ThemeService.Apply(mode);
                OnPropertyChanged();
            }
        }

        public List<string> ReaderThemeChoices { get; } = new() { "Comme l'application", "Sombre", "Clair", "Sépia", "Noir (OLED)" };

        public int ReaderThemeIndex
        {
            get => AppSettings.ReaderTheme switch { "dark" => 1, "light" => 2, "sepia" => 3, "black" => 4, _ => 0 };
            set
            {
                AppSettings.ReaderTheme = value switch { 1 => "dark", 2 => "light", 3 => "sepia", 4 => "black", _ => "app" };
                OnPropertyChanged();
            }
        }

        public double ReaderFontSize
        {
            get => AppSettings.ReaderFontSize;
            set { AppSettings.ReaderFontSize = (int)Math.Round(value); OnPropertyChanged(); OnPropertyChanged(nameof(ReaderFontSizeText)); }
        }

        public string ReaderFontSizeText => $"Taille du texte : {AppSettings.ReaderFontSize} px";

        public bool ReaderSerif
        {
            get => AppSettings.ReaderSerif;
            set { AppSettings.ReaderSerif = value; OnPropertyChanged(); }
        }

        public bool ReaderJustify
        {
            get => AppSettings.ReaderJustify;
            set { AppSettings.ReaderJustify = value; OnPropertyChanged(); }
        }

        public bool CompactList
        {
            get => AppSettings.CompactList;
            set { AppSettings.CompactList = value; OnPropertyChanged(); }
        }

        public bool ShowThumbnails
        {
            get => AppSettings.ShowThumbnails;
            set { AppSettings.ShowThumbnails = value; OnPropertyChanged(); }
        }

        public bool MarkReadOnOpen
        {
            get => AppSettings.MarkReadOnOpen;
            set { AppSettings.MarkReadOnOpen = value; OnPropertyChanged(); }
        }

        public bool AutoExtractOnOpen
        {
            get => AppSettings.AutoExtractOnOpen;
            set { AppSettings.AutoExtractOnOpen = value; OnPropertyChanged(); }
        }

        public bool ShowOnlineByDefault
        {
            get => AppSettings.ShowOnlineByDefault;
            set { AppSettings.ShowOnlineByDefault = value; OnPropertyChanged(); }
        }

        public List<string> OpenModeChoices { get; } = new() { "Lecteur intégré", "Navigateur" };

        public int OpenModeIndex
        {
            get => AppSettings.OpenMode;
            set { if (value >= 0) { AppSettings.OpenMode = value; OnPropertyChanged(); } }
        }

        // =================================================================
        // Rétention
        // =================================================================

        public static readonly int[] RetentionValues = { 0, 3, 7, 14, 30, 60, 90, 180, 365 };

        public List<string> RetentionChoices { get; } = new()
        {
            "Jamais", "3 jours", "7 jours", "14 jours", "30 jours", "60 jours", "90 jours", "6 mois", "1 an"
        };

        private static int IndexOf(int value)
        {
            int idx = Array.IndexOf(RetentionValues, value);
            return idx < 0 ? 0 : idx;
        }

        public int RetentionReadIndex
        {
            get => IndexOf(AppSettings.RetentionReadDays);
            set { if (value >= 0 && value < RetentionValues.Length) { AppSettings.RetentionReadDays = RetentionValues[value]; OnPropertyChanged(); } }
        }

        public int RetentionAllIndex
        {
            get => IndexOf(AppSettings.RetentionAllDays);
            set { if (value >= 0 && value < RetentionValues.Length) { AppSettings.RetentionAllDays = RetentionValues[value]; OnPropertyChanged(); } }
        }

        public bool AutoBackupEnabled
        {
            get => AppSettings.AutoBackupEnabled;
            set { AppSettings.AutoBackupEnabled = value; OnPropertyChanged(); }
        }

        // =================================================================
        // Filtres mots-clés
        // =================================================================

        public string MutedKeywords
        {
            get => AppSettings.MutedKeywords;
            set { AppSettings.MutedKeywords = value; OnPropertyChanged(); }
        }

        public string HighlightKeywords
        {
            get => AppSettings.HighlightKeywords;
            set { AppSettings.HighlightKeywords = value; OnPropertyChanged(); }
        }
    }
}
