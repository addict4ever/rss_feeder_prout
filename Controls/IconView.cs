using Microsoft.Maui.Controls.Shapes;
using Rss_feeder_prout.Helpers;
using ShapePath = Microsoft.Maui.Controls.Shapes.Path;

namespace Rss_feeder_prout.Controls
{
    /// <summary>
    /// Icône vectorielle (voir Helpers/IconPaths). Couleur et taille liables : suit le thème.
    /// Exemple : &lt;controls:IconView Icon="star" Color="{DynamicResource Accent}" Size="20" /&gt;
    /// </summary>
    public class IconView : ContentView
    {
        private static readonly PathGeometryConverter Converter = new();

        public static readonly BindableProperty IconProperty =
            BindableProperty.Create(nameof(Icon), typeof(string), typeof(IconView), "info",
                propertyChanged: (b, o, n) => ((IconView)b).UpdateGeometry());

        public static readonly BindableProperty ColorProperty =
            BindableProperty.Create(nameof(Color), typeof(Color), typeof(IconView), Colors.Gray,
                propertyChanged: (b, o, n) => ((IconView)b).UpdateBrush());

        public static readonly BindableProperty SizeProperty =
            BindableProperty.Create(nameof(Size), typeof(double), typeof(IconView), 22.0,
                propertyChanged: (b, o, n) => ((IconView)b).UpdateSize());

        public static readonly BindableProperty StrokeWidthProperty =
            BindableProperty.Create(nameof(StrokeWidth), typeof(double), typeof(IconView), 2.0,
                propertyChanged: (b, o, n) => ((IconView)b).UpdateBrush());

        public static readonly BindableProperty FilledProperty =
            BindableProperty.Create(nameof(Filled), typeof(bool), typeof(IconView), false,
                propertyChanged: (b, o, n) => ((IconView)b).UpdateBrush());

        public string Icon
        {
            get => (string)GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public Color Color
        {
            get => (Color)GetValue(ColorProperty);
            set => SetValue(ColorProperty, value);
        }

        public double Size
        {
            get => (double)GetValue(SizeProperty);
            set => SetValue(SizeProperty, value);
        }

        public double StrokeWidth
        {
            get => (double)GetValue(StrokeWidthProperty);
            set => SetValue(StrokeWidthProperty, value);
        }

        public bool Filled
        {
            get => (bool)GetValue(FilledProperty);
            set => SetValue(FilledProperty, value);
        }

        private readonly ShapePath _path;

        public IconView()
        {
            InputTransparent = true;
            HorizontalOptions = LayoutOptions.Center;
            VerticalOptions = LayoutOptions.Center;

            _path = new ShapePath
            {
                Aspect = Stretch.None,
                StrokeLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.Start,
                InputTransparent = true
            };
            Content = _path;

            UpdateGeometry();
            UpdateSize();
            UpdateBrush();
        }

        private void UpdateGeometry()
        {
            try
            {
                _path.Data = (Geometry)Converter.ConvertFromInvariantString(IconPaths.Get(Icon))!;
            }
            catch
            {
                _path.Data = (Geometry)Converter.ConvertFromInvariantString(IconPaths.Get("info"))!;
            }
        }

        private void UpdateSize()
        {
            var size = Size <= 0 ? 22 : Size;
            WidthRequest = size;
            HeightRequest = size;
            _path.WidthRequest = size;
            _path.HeightRequest = size;
            var scale = size / 24.0;
            _path.RenderTransform = new ScaleTransform(scale, scale);
        }

        private void UpdateBrush()
        {
            var color = Color ?? Colors.Gray;
            _path.Stroke = new SolidColorBrush(color);
            _path.StrokeThickness = StrokeWidth;
            _path.Fill = Filled ? new SolidColorBrush(color) : null;
        }
    }
}
