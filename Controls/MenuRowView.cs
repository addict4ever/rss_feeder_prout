using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using Rss_feeder_prout.Services;

namespace Rss_feeder_prout.Controls
{
    /// <summary>Ligne de menu : pastille d'icône + titre + sous-titre + chevron (ou interrupteur).</summary>
    public class MenuRowView : Grid
    {
        public static readonly BindableProperty IconProperty =
            BindableProperty.Create(nameof(Icon), typeof(string), typeof(MenuRowView), "info",
                propertyChanged: (b, o, n) => ((MenuRowView)b)._icon.Icon = (string)n);

        public static readonly BindableProperty TitleProperty =
            BindableProperty.Create(nameof(Title), typeof(string), typeof(MenuRowView), string.Empty,
                propertyChanged: (b, o, n) => ((MenuRowView)b)._title.Text = (string)n);

        public static readonly BindableProperty SubtitleProperty =
            BindableProperty.Create(nameof(Subtitle), typeof(string), typeof(MenuRowView), string.Empty,
                propertyChanged: (b, o, n) => ((MenuRowView)b).ApplySubtitle());

        public static readonly BindableProperty AccentProperty =
            BindableProperty.Create(nameof(Accent), typeof(Color), typeof(MenuRowView), null,
                propertyChanged: (b, o, n) => ((MenuRowView)b).ApplyColors());

        public static readonly BindableProperty CommandProperty =
            BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(MenuRowView), null);

        public static readonly BindableProperty CommandParameterProperty =
            BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(MenuRowView), null);

        public static readonly BindableProperty ShowChevronProperty =
            BindableProperty.Create(nameof(ShowChevron), typeof(bool), typeof(MenuRowView), true,
                propertyChanged: (b, o, n) => ((MenuRowView)b)._chevron.IsVisible = (bool)n);

        public static readonly BindableProperty IsDangerProperty =
            BindableProperty.Create(nameof(IsDanger), typeof(bool), typeof(MenuRowView), false,
                propertyChanged: (b, o, n) => ((MenuRowView)b).ApplyColors());

        public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
        public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public string Subtitle { get => (string)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
        public Color? Accent { get => (Color?)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
        public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
        public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }
        public bool ShowChevron { get => (bool)GetValue(ShowChevronProperty); set => SetValue(ShowChevronProperty, value); }
        public bool IsDanger { get => (bool)GetValue(IsDangerProperty); set => SetValue(IsDangerProperty, value); }

        public event EventHandler? Tapped;

        private readonly Border _badge;
        private readonly IconView _icon;
        private readonly Label _title;
        private readonly Label _subtitle;
        private readonly IconView _chevron;

        public MenuRowView()
        {
            Padding = new Thickness(14, 12);
            ColumnSpacing = 14;
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            };

            _icon = new IconView { Size = 20 };
            _badge = new Border
            {
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                WidthRequest = 40,
                HeightRequest = 40,
                VerticalOptions = LayoutOptions.Center,
                Content = _icon
            };

            _title = new Label { FontFamily = "OpenSansSemibold", FontSize = 15.5 };
            _subtitle = new Label { FontSize = 12.5, IsVisible = false, MaxLines = 2, LineBreakMode = LineBreakMode.TailTruncation };
            var texts = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center, Children = { _title, _subtitle } };

            _chevron = new IconView { Icon = "chevron-right", Size = 18, VerticalOptions = LayoutOptions.Center };

            Grid.SetColumn(_badge, 0);
            Grid.SetColumn(texts, 1);
            Grid.SetColumn(_chevron, 2);
            Children.Add(_badge);
            Children.Add(texts);
            Children.Add(_chevron);

            var tap = new TapGestureRecognizer();
            tap.Tapped += async (s, e) =>
            {
                try
                {
                    await this.FadeTo(0.55, 60);
                    await this.FadeTo(1, 120);
                }
                catch { }
                Tapped?.Invoke(this, EventArgs.Empty);
                if (Command?.CanExecute(CommandParameter) == true)
                    Command.Execute(CommandParameter);
            };
            GestureRecognizers.Add(tap);

            ApplyColors();
            Loaded += (s, e) => { ThemeService.ThemeChanged += OnThemeChanged; ApplyColors(); };
            Unloaded += (s, e) => ThemeService.ThemeChanged -= OnThemeChanged;
        }

        private void ApplySubtitle()
        {
            _subtitle.Text = Subtitle;
            _subtitle.IsVisible = !string.IsNullOrWhiteSpace(Subtitle);
        }

        private void OnThemeChanged(object? sender, EventArgs e) => ApplyColors();

        private void ApplyColors()
        {
            var p = ThemeService.Palette;
            var accent = IsDanger ? p.Danger : (Accent ?? p.Accent);
            _badge.BackgroundColor = accent.WithAlpha(0.15f);
            _icon.Color = accent;
            _title.TextColor = IsDanger ? p.Danger : p.TextPrimary;
            _subtitle.TextColor = p.TextMuted;
            _chevron.Color = p.TextMuted;
        }
    }
}
