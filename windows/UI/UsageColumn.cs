using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using UsageOrbit.Data;

namespace UsageOrbit.UI
{
    internal sealed class UsageColumn : StackPanel
    {
        private readonly bool claude;
        private readonly RingControl rings;
        private readonly MascotAnimator animator;
        private readonly TextBlock state;
        private readonly TextBlock instruction;
        private readonly Row session, weekly;
        private readonly Brush sessionColor, weeklyColor;

        public UsageColumn(bool claude)
        {
            this.claude = claude;
            animator = new MascotAnimator(claude);
            Margin = new Thickness(7, 0, 7, 0);
            sessionColor = Palette.Brush(claude ? "#E8835F" : "#6C9BFF");
            weeklyColor = Palette.Brush(claude ? "#F4C7AC" : "#B7CDFF");
            rings = new RingControl { HorizontalAlignment = HorizontalAlignment.Center, SessionBrush = sessionColor, WeeklyBrush = weeklyColor };
            Children.Add(rings);
            session = new Row(); weekly = new Row();
            Children.Add(session.Panel); Children.Add(weekly.Panel);
            state = Text("", 10.5);
            state.Margin = new Thickness(0, 3, 0, 0);
            state.TextTrimming = TextTrimming.CharacterEllipsis;
            instruction = Text("", 10.5);
            instruction.TextTrimming = TextTrimming.CharacterEllipsis;
            Children.Add(state); Children.Add(instruction);
        }

        public bool NeedsTicks(bool active) { return animator.NeedsTicks(active); }
        public void Animate(bool active)
        {
            animator.Advance(active);
            rings.Artwork = animator.Current;
            rings.InvalidateVisual();
        }
        public void Update(UsagePoller poller, Variant variant, ViewMode view)
        {
            var reading = poller.Reading;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            bool auth = reading.State == ReadState.NeedsAuth;
            bool stale = reading.LastSuccessAt.HasValue && (now - reading.LastSuccessAt.Value >= TimeSpan.FromMinutes(15) || reading.State != ReadState.Ok && reading.State != ReadState.NoData);
            rings.NeedsAuth = auth;
            rings.Muted = stale;
            rings.Session = DisplayValue(reading.Session, auth, now);
            rings.Weekly = DisplayValue(reading.Weekly, auth, now);
            rings.Artwork = variant == Variant.Logos ? Artwork.Logo(claude) : animator.Current;
            rings.ArtworkSize = variant == Variant.Logos ? 56 : 64;
            rings.InvalidateVisual();
            UpdateRow(session, reading.Session, "Sesión", sessionColor, auth, stale, now, view);
            UpdateRow(weekly, reading.Weekly, "Semana", weeklyColor, auth, stale, now, view);
            string age = reading.LastSuccessAt.HasValue ? "hace " + Age(now - reading.LastSuccessAt.Value) : "";
            int retry = (int)Math.Max(1, Math.Ceiling((poller.NextAttempt - now).TotalMinutes));
            string status;
            switch (reading.State)
            {
                case ReadState.NeedsAuth: status = "Sin sesión"; break;
                case ReadState.ConnectionError: status = "Sin conexión"; break;
                case ReadState.RateLimited: status = "Demasiadas consultas"; break;
                case ReadState.ServiceError: status = "Error del servicio"; break;
                case ReadState.NoData: status = "Sin datos" + (age.Length > 0 ? " · " + age : ""); break;
                default: status = (stale ? "Antiguo · " : "") + age; break;
            }
            // En la vista de solo imágenes queda visible únicamente lo que requiere atención.
            bool pending = !auth && (DisplayValue(reading.Session, false, now) == null || DisplayValue(reading.Weekly, false, now) == null);
            bool healthy = reading.State == ReadState.Ok && !stale && !pending;
            if (view == ViewMode.ImagesOnly && reading.State == ReadState.Ok && pending) status = "Falta actualizar";
            state.Text = view == ViewMode.Full && poller.Notice != null ? poller.Notice : status;
            state.Visibility = view == ViewMode.Full || !healthy ? Visibility.Visible : Visibility.Collapsed;
            state.Foreground = Palette.Brush(stale || reading.State == ReadState.RateLimited ? "#E8BA72" : "#ADB3C1");
            instruction.Text = auth ? (claude ? "claude auth login" : "codex login") :
                (stale && reading.State != ReadState.Ok && reading.State != ReadState.NoData ? "Antiguo · " + age : "");
            if (reading.State == ReadState.ConnectionError || reading.State == ReadState.RateLimited)
                instruction.Text = "Reintenta en " + retry + " min" + (stale ? " · Antiguo" : "");
            instruction.Foreground = Palette.Brush(stale ? "#E8BA72" : "#ADB3C1");
            instruction.Visibility = instruction.Text.Length > 0 && (view == ViewMode.Full || !healthy) ? Visibility.Visible : Visibility.Collapsed;
            string tooltip = status + "\n" + instruction.Text + "\n" + age + "\n" + (reading.Message ?? "") + (poller.Notice == null ? "" : "\n" + poller.Notice);
            state.ToolTip = tooltip; instruction.ToolTip = tooltip;
            System.Windows.Automation.AutomationProperties.SetName(rings, (claude ? "Claude Code" : "Codex") + ". " + session.Value.Text + " consumido en sesión. " + weekly.Value.Text + " consumido en semana.");
        }
        private static double? DisplayValue(UsageWindow window, bool auth, DateTimeOffset now)
        {
            return auth || window == null || window.ResetsAt <= now ? null : window.UsedPercent;
        }
        private static void UpdateRow(Row row, UsageWindow window, string name, Brush color, bool auth, bool stale, DateTimeOffset now, ViewMode view)
        {
            double? value = DisplayValue(window, auth, now);
            bool expired = !auth && window != null && window.ResetsAt <= now;
            row.Panel.Visibility = view == ViewMode.ImagesOnly ? Visibility.Collapsed : Visibility.Visible;
            row.Label.Text = name + (window != null && window.WindowMinutes.HasValue ? " " + Duration(window.WindowMinutes.Value) : "");
            row.Value.Text = !value.HasValue ? "—" : value > 0 && value < 0.05 ? "<0.1 %" : value.Value.ToString("0.#", CultureInfo.CurrentCulture) + " %";
            row.Value.Foreground = value > 90 ? Palette.Brush("#FF6B6B") : color;
            row.Dot.Fill = color;
            row.Panel.Opacity = stale && !expired ? 0.55 : 1;
            row.Reset.Text = auth ? "Sin sesión" : expired ? "Falta actualizar" :
                !value.HasValue ? "Sin datos" : window.ResetsAt.HasValue ? "en " + Remaining(window.ResetsAt.Value - now) : "Sin reinicio";
            row.Panel.ToolTip = (expired ? "Reinició · falta actualizar.\n" : "") + name + ": " + row.Value.Text + " consumido. " +
                (window != null && window.ResetsAt.HasValue ? "Reinicio: " + window.ResetsAt.Value.ToLocalTime().ToString("dddd d MMMM yyyy HH:mm:ss zzz", CultureInfo.GetCultureInfo("es-MX")) : "Sin fecha de reinicio.");
        }
        internal static string Duration(int minutes)
        {
            if (minutes % 1440 == 0) return (minutes / 1440) + " d";
            if (minutes % 60 == 0) return (minutes / 60) + " h";
            return minutes + " min";
        }
        internal static string Remaining(TimeSpan time)
        {
            int minutes = (int)Math.Max(1, Math.Ceiling(time.TotalMinutes));
            if (minutes >= 1440) return (minutes / 1440) + " d " + (minutes % 1440 / 60) + " h";
            if (minutes >= 60) return (minutes / 60) + " h " + (minutes % 60) + " min";
            return minutes + " min";
        }
        private static string Age(TimeSpan time)
        {
            if (time.TotalMinutes < 1) return "menos de 1 min";
            if (time.TotalHours < 1) return ((int)time.TotalMinutes) + " min";
            if (time.TotalDays < 1) return ((int)time.TotalHours) + " h";
            return ((int)time.TotalDays) + " d";
        }
        internal static TextBlock Text(string text, double size)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = Palette.Brush("#F2F4F8"), VerticalAlignment = VerticalAlignment.Center };
        }
        private sealed class Row
        {
            public readonly Grid Panel = new Grid { Height = 22 };
            public readonly Ellipse Dot = new Ellipse { Width = 5, Height = 5, VerticalAlignment = VerticalAlignment.Center };
            public readonly TextBlock Label = Text("", 11);
            public readonly TextBlock Value = Text("", 12);
            public readonly TextBlock Reset = Text("", 10);
            public Row()
            {
                Panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                Panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(61) });
                Panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
                Panel.ColumnDefinitions.Add(new ColumnDefinition());
                Value.FontFamily = new FontFamily("Segoe UI Semibold");
                Reset.Foreground = Palette.Brush("#ADB3C1");
                Reset.TextTrimming = TextTrimming.CharacterEllipsis;
                UIElement[] cells = { Dot, Label, Value, Reset };
                for (int i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); Panel.Children.Add(cells[i]); }
            }
        }
    }
}
