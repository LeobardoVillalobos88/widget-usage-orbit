using System;
using System.Windows;
using System.Windows.Media;

namespace UsageOrbit.UI
{
    internal sealed class RingControl : FrameworkElement
    {
        public double? Session { get; set; }
        public double? Weekly { get; set; }
        public Brush SessionBrush { get; set; }
        public Brush WeeklyBrush { get; set; }
        public bool Muted { get; set; }
        public bool NeedsAuth { get; set; }
        public ImageSource Artwork { get; set; }
        public double ArtworkSize { get; set; }
        public RingControl() { Width = 112; Height = 112; RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality); }

        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            context.PushOpacity(Muted ? 0.45 : 1);
            Ring(context, 51, Weekly, WeeklyBrush);
            Ring(context, 40, Session, SessionBrush);
            context.Pop();
            if (Artwork != null) context.DrawImage(Artwork, new Rect(56 - ArtworkSize / 2, 56 - ArtworkSize / 2, ArtworkSize, ArtworkSize));
        }
        private void Ring(DrawingContext context, double radius, double? value, Brush color)
        {
            var track = new Pen(new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)), 7);
            if (!value.HasValue && !NeedsAuth) track.DashStyle = new DashStyle(new[] { 0.3, 1.2 }, 0);
            track.StartLineCap = track.EndLineCap = track.DashCap = PenLineCap.Round;
            context.DrawGeometry(null, track, Arc(radius, 100));
            if (!value.HasValue || value <= 0) return;
            var pen = new Pen(value > 90 ? Palette.Brush("#FF6B6B") : color, 7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            context.DrawGeometry(null, pen, Arc(radius, Math.Min(100, value.Value)));
        }
        private static StreamGeometry Arc(double radius, double percent)
        {
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                path.BeginFigure(new Point(56, 56 - radius), false, false);
                if (percent >= 100)
                {
                    path.ArcTo(new Point(56, 56 + radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
                    path.ArcTo(new Point(56, 56 - radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
                }
                else
                {
                    double angle = percent / 100 * Math.PI * 2;
                    path.ArcTo(new Point(56 + radius * Math.Sin(angle), 56 - radius * Math.Cos(angle)), new Size(radius, radius), 0, percent > 50, SweepDirection.Clockwise, true, false);
                }
            }
            geometry.Freeze();
            return geometry;
        }
    }
    internal static class Palette
    {
        public static SolidColorBrush Brush(string value)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            brush.Freeze();
            return brush;
        }
    }
}
