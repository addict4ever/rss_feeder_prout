using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using Rss_feeder_prout.Services;

namespace Rss_feeder_prout.Controls
{
    /// <summary>Bouton rond contenant une icône vectorielle.</summary>
    public class IconButton : Border
    {
        public static readonly BindableProperty IconProperty =
            BindableProperty.Create(nameof(Icon), typeof(string), typeof(IconButton), "info",
                propertyChanged: (b, o, n) => ((IconButton)b)._icon.Icon = (string)n);

        public static readonly BindableProperty IconColorProperty =
            BindableProperty.Create(nameof(IconColor), typeof(Color), typeof(IconButton), null,
                propertyChanged: (b, o, n) => ((IconButton)b).ApplyColors());

        public static readonly BindableProperty IconSizeProperty =
            BindableProperty.Create(nameof(IconSize), typeof(double), typeof(IconButton), 21.0,
                propertyChanged: (b, o, n) => ((IconButton)b)._icon.Size = (double)n);

        public static readonly BindableProperty FilledProperty =
            BindableProperty.Create(nameof(Filled), typeof(bool), typeof(IconButton), false,
                propertyChanged: (b, o, n) => ((IconButton)b)._icon.Filled = (bool)n);

        public static readonly BindableProperty SizeProperty =
            BindableProperty.Create(nameof(Size), typeof(double), typeof(IconButton), 42.0,
                propertyChanged: (b, o, n) => ((IconButton)b).ApplySize());

        public static readonly BindableProperty CommandProperty =
            BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(IconButton), null);

        public static readonly BindableProperty CommandParameterProperty =
            BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(IconButton), null);

        /// <summary>Style "plein" (fond surface) ou transparent.</summary>
        public static readonly BindableProperty IsFlatProperty =
            BindableProperty.Create(nameof(IsFlat), typeof(bool), typeof(IconButton), false,
                propertyChanged: (b, o, n) => ((IconButton)b).ApplyColors());

        public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
        public Color? IconColor { get => (Color?)GetValue(IconColorProperty); set => SetValue(IconColorProperty, value); }
        public double IconSize { get => (double)GetValue(IconSizeProperty); set => SetValue(IconSizeProperty, value); }
        public bool Filled { get => (bool)GetValue(FilledProperty); set => SetValue(FilledProperty, value); }
        public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
        public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
        public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }
        public bool IsFlat { get => (bool)GetValue(IsFlatProperty); set => SetValue(IsFlatProperty, value); }

        public event EventHandler? Clicked;

        private readonly IconView _icon;

        public IconButton()
        {
            StrokeThickness = 0;
            StrokeShape = new RoundRectangle { CornerRadius = 14 };
            Padding = 0;
            HorizontalOptions = LayoutOptions.Center;
            VerticalOptions = LayoutOptions.Center;

            _icon = new IconView { Icon = Icon, Size = IconSize };
            Content = _icon;

            var tap = new TapGestureRecognizer();
            tap.Tapped += OnTapped;
            GestureRecognizers.Add(tap);

            ApplySize();
            ApplyColors();
            Loaded += (s, e) => { ThemeService.ThemeChanged += OnThemeChanged; ApplyColors(); };
            Unloaded += (s, e) => ThemeService.ThemeChanged -= OnThemeChanged;
        }

        private async void OnTapped(object? sender, TappedEventArgs e)
        {
            if (!IsEnabled) return;

            try
            {
                await this.ScaleTo(0.88, 60, Easing.CubicOut);
                await this.ScaleTo(1, 90, Easing.CubicOut);
            }
            catch { }

            Clicked?.Invoke(this, EventArgs.Empty);
            if (Command?.CanExecute(CommandParameter) == true)
                Command.Execute(CommandParameter);
        }

        private void ApplySize()
        {
            WidthRequest = Size;
            HeightRequest = Size;
        }

        private void OnThemeChanged(object? sender, EventArgs e) => ApplyColors();

        private void ApplyColors()
        {
            var p = ThemeService.Palette;
            BackgroundColor = IsFlat ? Colors.Transparent : p.Surface2;
            _icon.Color = IconColor ?? p.TextPrimary;
        }
    }
}
