using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using System.Diagnostics;

namespace Rss_feeder_prout.Services
{
    /// <summary>Accès aux services hors injection (contrôles XAML, services Android).</summary>
    public static class ServiceHelper
    {
        public static IServiceProvider? Services => IPlatformApplication.Current?.Services;

        public static T? Get<T>() where T : class => Services?.GetService<T>();
    }

    /// <summary>Dialogues sécurisés (thread UI + Shell absent en arrière-plan) et messages rapides.</summary>
    public static class UiHelper
    {
        private static Page? CurrentPage => Shell.Current?.CurrentPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;

        public static Task AlertAsync(string title, string message, string cancel = "OK")
        {
            return MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var page = CurrentPage;
                if (page != null) await page.DisplayAlert(title, message, cancel);
            });
        }

        public static Task<bool> ConfirmAsync(string title, string message, string accept = "Oui", string cancel = "Annuler")
        {
            return MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var page = CurrentPage;
                if (page == null) return false;
                return await page.DisplayAlert(title, message, accept, cancel);
            });
        }

        public static Task<string?> ActionSheetAsync(string title, string cancel, string? destruction, params string[] buttons)
        {
            return MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var page = CurrentPage;
                if (page == null) return (string?)null;
                var result = await page.DisplayActionSheet(title, cancel, destruction, buttons);
                return result == cancel ? null : result;
            });
        }

        public static Task<string?> PromptAsync(string title, string message, string initialValue = "", string placeholder = "",
                                                Keyboard? keyboard = null, int maxLength = -1)
        {
            return MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var page = CurrentPage;
                if (page == null) return (string?)null;
                return await page.DisplayPromptAsync(title, message, "OK", "Annuler", placeholder, maxLength, keyboard ?? Keyboard.Default, initialValue);
            });
        }

        /// <summary>Petit message non bloquant (Toast Android).</summary>
        public static void Toast(string message)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
#if ANDROID
                    Android.Widget.Toast.MakeText(Android.App.Application.Context, message, Android.Widget.ToastLength.Short)?.Show();
#else
                    Debug.WriteLine($"[TOAST] {message}");
#endif
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[TOAST ERROR] {ex.Message}");
                }
            });
        }

        public static async Task GoToAsync(string route, IDictionary<string, object>? parameters = null)
        {
            try
            {
                if (Shell.Current == null) return;
                if (parameters != null)
                    await Shell.Current.GoToAsync(route, true, parameters);
                else
                    await Shell.Current.GoToAsync(route, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NAV ERROR] {route} : {ex.Message}");
            }
        }

        public static async Task GoBackAsync()
        {
            try
            {
                if (Shell.Current?.Navigation.NavigationStack.Count > 1)
                    await Shell.Current.GoToAsync("..");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NAV ERROR] back : {ex.Message}");
            }
        }

        public static async Task OpenBrowserAsync(string? url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            {
                await AlertAsync("Lien invalide", "L'adresse Web est mal formée.");
                return;
            }

            try
            {
                await Browser.Default.OpenAsync(uri, new BrowserLaunchOptions
                {
                    LaunchMode = BrowserLaunchMode.SystemPreferred,
                    TitleMode = BrowserTitleMode.Show,
                    PreferredToolbarColor = ThemeService.Palette.Bg,
                    PreferredControlColor = ThemeService.Palette.Accent
                });
            }
            catch
            {
                try { await Launcher.Default.OpenAsync(uri); }
                catch { await AlertAsync("Erreur", "Impossible d'ouvrir le lien."); }
            }
        }
    }
}
