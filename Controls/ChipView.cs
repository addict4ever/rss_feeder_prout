using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using Rss_feeder_prout.Services;

namespace Rss_feeder_prout.Controls
{
    /// <summary>Pastille de filtre sélectionnable (état visuel géré en code : fiable sur toutes les versions).</summary>
    public class ChipView : Border
    {
        public static readonly BindableProperty TextProperty =
            BindableProperty.Create(nameof(Text), typeof(string), typeof(ChipView), string.Empty,
                propertyChanged: (b, o, n) => ((ChipView)b)._label.Text = (string)n);

        public static readonly BindableProperty IconProperty =
            BindableProperty.Create(nameof(Icon), typeof(string), typeof(ChipView), null,
                propertyChanged: (b, o, n) => ((ChipView)b).ApplyIcon());

        public static readonly BindableProperty IsSelectedProperty =
            BindableProperty.Create(nameof(IsSelected), typeof(bool), typeof(ChipView), false,
                propertyChanged: (b, o, n) => ((ChipView)b).ApplyState());

        public static readonly BindableProperty CommandProperty =
            BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(ChipView), null);

        public static readonly BindableProperty CommandParameterProperty =
            BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(ChipView), null);

        public static readonly BindableProperty CountProperty =
            BindableProperty.Create(nameof(Count), typeof(string), typeof(ChipView), null,
                propertyChanged: (b, o, n) => ((ChipView)b).ApplyCount());

        public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
        public string? Icon { get => (string?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
        public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
        public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
        public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }
        public string? Count { get => (string?)GetValue(CountProperty); set => SetValue(CountProperty, value); }

        private readonly Label _label;
        private readonly Label _count;
        private readonly IconView _icon;

        public ChipView()
        {
            StrokeShape = new RoundRectangle { CornerRadius = 999 };
            StrokeThickness = 1;
            Padding = new Thickness(14, 0);
            HeightRequest = 36;
            VerticalOptions = LayoutOptions.Center;

            _icon = new IconView { Size = 15, StrokeWidth = 2.2, IsVisible = false, Margin = new Thickness(0, 0, 6, 0) };
            _label = new Label { FontFamily = "OpenSansSemibold", FontSize = 13, VerticalOptions = LayoutOptions.Center };
            _count = new Label { FontFamily = "OpenSansSemibold", FontSize = 12, VerticalOptions = LayoutOptions.Center, Margin = new Thickness(6, 0, 0, 0), IsVisible = false };

            Content = new HorizontalStackLayout
            {
                VerticalOptions = LayoutOptions.Center,
                Children = { _icon, _label, _count }
            };

            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, e) =>
            {
                if (Command?.CanExecute(CommandParameter) == true)
                    Command.Execute(CommandParameter);
            };
            GestureRecognizers.Add(tap);

            ApplyState();
            Loaded += (s, e) => { ThemeService.ThemeChanged += OnThemeChanged; ApplyState(); };
            Unloaded += (s, e) => ThemeService.ThemeChanged -= OnThemeChanged;
        }

        private void ApplyIcon()
        {
            _icon.IsVisible = !string.IsNullOrEmpty(Icon);
            if (!string.IsNullOrEmpty(Icon)) _icon.Icon = Icon!;
        }

        private void ApplyCount()
        {
            _count.Text = Count ?? string.Empty;
            _count.IsVisible = !string.IsNullOrEmpty(Count) && Count != "0";
        }

        private void OnThemeChanged(object? sender, EventArgs e) => ApplyState();

        private void ApplyState()
        {
            var p = ThemeService.Palette;
            BackgroundColor = IsSelected ? p.Accent : p.Surface;
            Stroke = new SolidColorBrush(IsSelected ? p.Accent : p.Border);
            _label.TextColor = IsSelected ? p.OnAccent : p.TextPrimary;
            _count.TextColor = IsSelected ? p.OnAccent : p.TextMuted;
            _icon.Color = IsSelected ? p.OnAccent : p.TextSecondary;
        }
    }
}
