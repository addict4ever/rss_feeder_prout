using Microsoft.Maui.Graphics;
using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
#if ANDROID
using Microsoft.Maui.Platform;
#endif

namespace Rss_feeder_prout.Services
{
    /// <summary>Palette de couleurs d'un thème.</summary>
    public sealed class ThemePalette
    {
        public bool IsDark { get; init; }
        public Color Bg { get; init; } = Colors.Black;
        public Color Surface { get; init; } = Colors.Black;
        public Color Surface2 { get; init; } = Colors.Black;
        public Color Surface3 { get; init; } = Colors.Black;
        public Color Border { get; init; } = Colors.Gray;
        public Color TextPrimary { get; init; } = Colors.White;
        public Color TextSecondary { get; init; } = Colors.LightGray;
        public Color TextMuted { get; init; } = Colors.Gray;
        public Color Accent { get; init; } = Colors.Orange;
        public Color AccentSoft { get; init; } = Colors.Orange;
        public Color OnAccent { get; init; } = Colors.White;
        public Color Success { get; init; } = Colors.Green;
        public Color SuccessSoft { get; init; } = Colors.Green;
        public Color Warning { get; init; } = Colors.Orange;
        public Color WarningSoft { get; init; } = Colors.Orange;
        public Color Info { get; init; } = Colors.Blue;
        public Color InfoSoft { get; init; } = Colors.Blue;
        public Color Danger { get; init; } = Colors.Red;
        public Color DangerSoft { get; init; } = Colors.Red;
        public Color Overlay { get; init; } = Colors.Black;
        public Color TabBar { get; init; } = Colors.Black;
    }

    /// <summary>
    /// Gère le thème (sombre / clair / système). Les couleurs sont injectées dans
    /// Application.Resources sous forme de clés utilisées en DynamicResource partout dans l'UI.
    /// </summary>
    public static class ThemeService
    {
        public static readonly ThemePalette Dark = new()
        {
            IsDark = true,
            Bg = Color.FromArgb("#0E1014"),
            Surface = Color.FromArgb("#171A20"),
            Surface2 = Color.FromArgb("#20242C"),
            Surface3 = Color.FromArgb("#2A2F39"),
            Border = Color.FromArgb("#2B303A"),
            TextPrimary = Color.FromArgb("#ECEEF2"),
            TextSecondary = Color.FromArgb("#A6ACB8"),
            TextMuted = Color.FromArgb("#6E7582"),
            Accent = Color.FromArgb("#FF8A3D"),
            AccentSoft = Color.FromArgb("#33FF8A3D"),
            OnAccent = Color.FromArgb("#1A0E05"),
            Success = Color.FromArgb("#3DD68C"),
            SuccessSoft = Color.FromArgb("#263DD68C"),
            Warning = Color.FromArgb("#F5B544"),
            WarningSoft = Color.FromArgb("#26F5B544"),
            Info = Color.FromArgb("#5AA9FF"),
            InfoSoft = Color.FromArgb("#265AA9FF"),
            Danger = Color.FromArgb("#FF5C6C"),
            DangerSoft = Color.FromArgb("#26FF5C6C"),
            Overlay = Color.FromArgb("#B3000000"),
            TabBar = Color.FromArgb("#13161B")
        };

        public static readonly ThemePalette Light = new()
        {
            IsDark = false,
            Bg = Color.FromArgb("#F6F4EF"),
            Surface = Color.FromArgb("#FFFFFF"),
            Surface2 = Color.FromArgb("#EFECE6"),
            Surface3 = Color.FromArgb("#E4E0D8"),
            Border = Color.FromArgb("#E3DFD7"),
            TextPrimary = Color.FromArgb("#1C1D21"),
            TextSecondary = Color.FromArgb("#575C68"),
            TextMuted = Color.FromArgb("#8D919B"),
            Accent = Color.FromArgb("#E8702A"),
            AccentSoft = Color.FromArgb("#24E8702A"),
            OnAccent = Color.FromArgb("#FFFFFF"),
            Success = Color.FromArgb("#1E9E62"),
            SuccessSoft = Color.FromArgb("#1F1E9E62"),
            Warning = Color.FromArgb("#B7791F"),
            WarningSoft = Color.FromArgb("#24D69E2E"),
            Info = Color.FromArgb("#2F6FD1"),
            InfoSoft = Color.FromArgb("#1F2F6FD1"),
            Danger = Color.FromArgb("#D83A4A"),
            DangerSoft = Color.FromArgb("#1FD83A4A"),
            Overlay = Color.FromArgb("#80000000"),
            TabBar = Color.FromArgb("#FFFFFF")
        };

        public static ThemePalette Palette { get; private set; } = Dark;

        public static event EventHandler? ThemeChanged;

        /// <summary>Applique le thème enregistré (ou celui passé en paramètre).</summary>
        public static void Apply(string? mode = null)
        {
            var app = Application.Current;
            if (app == null) return;

            mode ??= AppSettings.AppTheme;

            bool dark;
            if (mode == "system")
            {
                app.UserAppTheme = AppTheme.Unspecified;
                dark = app.RequestedTheme != AppTheme.Light;
            }
            else
            {
                dark = mode != "light";
                app.UserAppTheme = dark ? AppTheme.Dark : AppTheme.Light;
            }

            Palette = dark ? Dark : Light;

            var r = app.Resources;
            r["Bg"] = Palette.Bg;
            r["Surface"] = Palette.Surface;
            r["Surface2"] = Palette.Surface2;
            r["Surface3"] = Palette.Surface3;
            r["BorderColor"] = Palette.Border;
            r["TextPrimary"] = Palette.TextPrimary;
            r["TextSecondary"] = Palette.TextSecondary;
            r["TextMuted"] = Palette.TextMuted;
            r["Accent"] = Palette.Accent;
            r["AccentSoft"] = Palette.AccentSoft;
            r["OnAccent"] = Palette.OnAccent;
            r["Success"] = Palette.Success;
            r["SuccessSoft"] = Palette.SuccessSoft;
            r["Warning"] = Palette.Warning;
            r["WarningSoft"] = Palette.WarningSoft;
            r["Info"] = Palette.Info;
            r["InfoSoft"] = Palette.InfoSoft;
            r["Danger"] = Palette.Danger;
            r["DangerSoft"] = Palette.DangerSoft;
            r["Overlay"] = Palette.Overlay;
            r["TabBarColor"] = Palette.TabBar;

            ApplyStatusBar();
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Calcule la marge haute des en-têtes (Android 15+ = affichage bord à bord).</summary>
        public static void ApplyInsets()
        {
            var app = Application.Current;
            if (app == null) return;

            double top = 8;
#if ANDROID
            try
            {
                if (OperatingSystem.IsAndroidVersionAtLeast(35))
                {
                    var res = Android.App.Application.Context.Resources;
                    if (res != null)
                    {
                        int id = res.GetIdentifier("status_bar_height", "dimen", "android");
                        if (id > 0)
                        {
                            float px = res.GetDimensionPixelSize(id);
                            float density = res.DisplayMetrics?.Density ?? 1f;
                            top += px / density;
                        }
                    }
                }
            }
            catch { }
#endif
            app.Resources["HeaderPadding"] = new Thickness(12, top, 12, 6);
            app.Resources["TopInset"] = top;
        }

        public static void ApplyStatusBar()
        {
#if ANDROID
            try
            {
                var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
                var window = activity?.Window;
                if (window == null) return;

#pragma warning disable CA1422
                window.SetStatusBarColor(Palette.Bg.ToPlatform());
                window.SetNavigationBarColor(Palette.TabBar.ToPlatform());
#pragma warning restore CA1422

                var controller = AndroidX.Core.View.WindowCompat.GetInsetsController(window, window.DecorView);
                if (controller != null)
                {
                    controller.AppearanceLightStatusBars = !Palette.IsDark;
                    controller.AppearanceLightNavigationBars = !Palette.IsDark;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[THEME] Status bar: {ex.Message}");
            }
#endif
        }
    }
}
