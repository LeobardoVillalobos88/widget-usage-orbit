using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using UsageOrbit.Data;
using UsageOrbit.Platform;
using UsageOrbit.UI;

namespace UsageOrbit.App
{
    internal sealed class WidgetWindow : Window
    {
        private const double FullWidth = 412, ImagesOnlyWidth = 284, SingleWidth = 250;
        private readonly bool testing;
        private readonly Settings settings;
        private readonly UsagePoller claude, codex;
        private readonly UsageColumn claudeColumn, codexColumn;
        private readonly DispatcherTimer timer, animationTimer;
        private readonly ActivityMonitor claudeActivity, codexActivity;
        private readonly bool alwaysActive;
        private readonly Border panel, tab;
        private readonly Grid columns;
        private readonly Ellipse claudeDot, codexDot;
        private readonly TextBlock tabArrow;
        private readonly Button logos, mascots;
        private IntPtr handle;
        private bool positioning, ready, closed, tabDragging, tabMoved;
        private Native.Point dragStart;
        private int dragTop;

        public WidgetWindow(string scenario, int? x, int? y)
        {
            testing = scenario != null;
            settings = testing ? new Settings() : Settings.Load();
            if (x.HasValue) settings.X = x;
            if (y.HasValue) settings.Y = y;
            if (scenario == "mascotas" || scenario == "animacion") settings.Variant = Variant.Mascots;
            alwaysActive = scenario == "animacion";
            if (scenario == "soloclaude") settings.Show = Assistants.Claude;
            if (scenario == "solocodex") settings.Show = Assistants.Codex;
            claude = new UsagePoller(testing ? null : new ClaudeUsageSource(), testing ? TestData.Create(scenario, true) : null);
            codex = new UsagePoller(testing ? null : new CodexSource(), testing ? TestData.Create(scenario, false) : null);
            Title = "Usage Orbit";
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            Topmost = true; ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize;
            FontFamily = new FontFamily("Segoe UI"); UseLayoutRounding = true;
            panel = Surface(new CornerRadius(18));
            var stack = new StackPanel { Margin = new Thickness(7, 4, 7, 5) };
            panel.Child = stack;
            if (testing)
            {
                var banner = UsageColumn.Text("DATOS DE PRUEBA", 10);
                banner.Foreground = Palette.Brush("#E8BA72"); banner.Height = 18;
                banner.HorizontalAlignment = HorizontalAlignment.Center; stack.Children.Add(banner);
            }
            var toolbar = new DockPanel { Height = 22, Opacity = 0.45 };
            toolbar.MouseEnter += delegate { toolbar.Opacity = 1; };
            toolbar.MouseLeave += delegate { toolbar.Opacity = 0.45; };
            var variants = new StackPanel { Orientation = Orientation.Horizontal };
            logos = Theme.Button("Logos", "Mostrar logotipos", false);
            mascots = Theme.Button("Mascotas", "Mostrar mascotas", false);
            logos.Click += delegate { SetVariant(Variant.Logos); };
            mascots.Click += delegate { SetVariant(Variant.Mascots); };
            variants.Children.Add(logos); variants.Children.Add(UsageColumn.Text("|", 10)); variants.Children.Add(mascots);
            if (settings.FixedVariant) variants.Visibility = Visibility.Collapsed;
            DockPanel.SetDock(variants, System.Windows.Controls.Dock.Left); toolbar.Children.Add(variants);
            var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            AddButton(controls, "\uE890", "Cambiar vista: completa o solo imágenes", delegate { SetView(settings.View == ViewMode.Full ? ViewMode.ImagesOnly : ViewMode.Full); });
            AddButton(controls, "\uE72C", "Actualizar", delegate { Refresh(); });
            AddButton(controls, "\uE76B", "Ocultar a la izquierda", delegate { Dock("left"); });
            AddButton(controls, "\uE76C", "Ocultar a la derecha", delegate { Dock("right"); });
            AddButton(controls, "\uE712", "Más", delegate { OpenMenu(); });
            AddButton(controls, "\uE711", "Cerrar", delegate { Close(); });
            toolbar.Children.Add(controls); stack.Children.Add(toolbar);
            columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition()); columns.ColumnDefinitions.Add(new ColumnDefinition());
            claudeColumn = new UsageColumn(true); codexColumn = new UsageColumn(false);
            Grid.SetColumn(codexColumn, 1); columns.Children.Add(claudeColumn); columns.Children.Add(codexColumn);
            stack.Children.Add(columns);
            tab = Surface(new CornerRadius(6));
            tab.ToolTip = "Usage Orbit · clic para restaurar; arrastra para mover";
            tab.Focusable = true;
            var tabContent = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            tabArrow = UsageColumn.Text("", 12); tabArrow.HorizontalAlignment = HorizontalAlignment.Center;
            tabContent.Children.Add(tabArrow);
            claudeDot = Dot(); codexDot = Dot(); tabContent.Children.Add(claudeDot); tabContent.Children.Add(codexDot);
            tab.Child = tabContent;
            tab.MouseLeftButtonDown += TabDown; tab.MouseMove += TabMove; tab.MouseLeftButtonUp += TabUp;
            tab.LostMouseCapture += delegate { tabDragging = false; };
            tab.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter || e.Key == Key.Space) { Restore(); e.Handled = true; } };
            Content = panel;
            PreviewMouseLeftButtonDown += DragPanel;
            ContextMenuOpening += delegate { ContextMenu = BuildMenu(); };
            ContextMenu = BuildMenu();
            SourceInitialized += OnSourceInitialized;
            LocationChanged += delegate { if (ready && !positioning && settings.DockSide == null) CapturePosition(); };
            SystemEvents.DisplaySettingsChanged += DisplayChanged;
            claude.Changed += Update; codex.Changed += Update;
            ApplyAssistants();
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            timer.Tick += delegate {
                claude.Tick(); codex.Tick(); Update();
                if (claudeActivity != null) { claudeActivity.Retry(); codexActivity.Retry(); }
            };
            animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            animationTimer.Tick += delegate { AnimateMascots(); };
            if (!testing)
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string claudeHome = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
                if (string.IsNullOrWhiteSpace(claudeHome)) claudeHome = System.IO.Path.Combine(home, ".claude");
                claudeActivity = new ActivityMonitor(System.IO.Path.Combine(claudeHome, "projects"));
                codexActivity = new ActivityMonitor(System.IO.Path.Combine(home, ".codex", "sessions"));
                claudeActivity.Started += ActivityStarted;
                codexActivity.Started += ActivityStarted;
            }
            Loaded += delegate { timer.Start(); claude.Tick(); codex.Tick(); Update(); UpdateAnimationTimer(); };
            Update();
        }
        private static Border Surface(CornerRadius radius)
        {
            return new Border { Background = Palette.Brush("#16171B"), BorderBrush = Palette.Brush("#2AFFFFFF"), BorderThickness = new Thickness(1), CornerRadius = radius };
        }
        private static Ellipse Dot() { return new Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 7, 0, 0) }; }
        private static void AddButton(Panel parent, string glyph, string hint, Action action)
        {
            Button button = Theme.Button(glyph, hint, true);
            button.Click += delegate { action(); };
            parent.Children.Add(button);
        }
        private void OnSourceInitialized(object sender, EventArgs e)
        {
            handle = new WindowInteropHelper(this).Handle;
            Native.ToolWindow(handle);
            if (!settings.X.HasValue || !settings.Y.HasValue)
            {
                Native.Rect area = Native.PrimaryWorkArea();
                double scale = Native.Scale(handle);
                settings.X = area.Right - (int)Math.Round((PanelSize().Width + 24) * scale);
                settings.Y = area.Top + (int)Math.Round(24 * scale);
            }
            ready = true;
            ApplyDockContent();
            Position();
        }
        private Size PanelSize()
        {
            double width = settings.Show != Assistants.Both ? SingleWidth : settings.View == ViewMode.ImagesOnly ? ImagesOnlyWidth : FullWidth;
            // Aplica antes los cambios de visibilidad pendientes; si no, se reutiliza la medida anterior.
            panel.UpdateLayout();
            panel.InvalidateMeasure();
            panel.Measure(new Size(width, double.PositiveInfinity));
            return new Size(width, Math.Ceiling(panel.DesiredSize.Height));
        }
        private void ApplyDockContent()
        {
            bool docked = settings.DockSide != null;
            Size size = PanelSize();
            Content = docked ? tab : panel;
            Width = docked ? 20 : size.Width;
            Height = docked ? 84 : size.Height;
            tabArrow.Text = settings.DockSide == "left" ? "›" : "‹";
        }
        private void Position()
        {
            if (!ready || positioning || closed) return;
            positioning = true;
            try
            {
                double scale = Native.Scale(handle);
                Size size = PanelSize();
                int w = (int)Math.Round(Width * scale), h = (int)Math.Round(Height * scale);
                int x = settings.X ?? 0, y = settings.Y ?? 0;
                Native.Rect target = new Native.Rect { Left = x, Top = y, Right = x + (int)(size.Width * scale), Bottom = y + (int)(size.Height * scale) };
                Native.Rect area = Native.WorkArea(target);
                if (settings.DockSide != null)
                {
                    x = settings.DockSide == "left" ? area.Left : area.Right - w;
                    y = settings.DockY ?? (y + (int)((size.Height - 84) * scale / 2));
                }
                x = Math.Max(area.Left, Math.Min(x, area.Right - w));
                y = Math.Max(area.Top, Math.Min(y, area.Bottom - h));
                Native.Place(handle, x, y, w, h);
                if (settings.DockSide == null) { settings.X = x; settings.Y = y; }
                else settings.DockY = y;
            }
            finally { positioning = false; }
        }
        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            Dispatcher.BeginInvoke(new Action(Position), DispatcherPriority.Loaded);
        }
        private void DisplayChanged(object sender, EventArgs e)
        {
            if (!closed) Dispatcher.BeginInvoke(new Action(delegate { Position(); Save(); }));
        }
        private void CapturePosition()
        {
            Native.Rect rect = Native.Bounds(handle);
            settings.X = rect.Left; settings.Y = rect.Top;
        }
        private void DragPanel(object sender, MouseButtonEventArgs e)
        {
            if (settings.DockSide != null) return;
            DependencyObject node = e.OriginalSource as DependencyObject;
            while (node != null)
            {
                if (node is ButtonBase || node is MenuItem) return;
                node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
            }
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove(); CapturePosition(); Position(); Save();
            }
        }
        private void Dock(string side)
        {
            if (settings.DockSide == null)
            {
                CapturePosition();
                Native.Rect bounds = Native.Bounds(handle);
                settings.DockY = bounds.Top + (bounds.Height - (int)(84 * Native.Scale(handle))) / 2;
            }
            settings.DockSide = side;
            ApplyDockContent(); Position(); Save();
            UpdateAnimationTimer();
        }
        private void Restore()
        {
            settings.DockSide = null; ApplyDockContent(); Position(); Save();
            UpdateAnimationTimer();
        }
        public void RestoreAndActivate()
        {
            if (closed) return;
            Restore(); Activate(); Native.SetForegroundWindow(handle);
        }
        private void TabDown(object sender, MouseButtonEventArgs e)
        {
            Native.GetCursorPos(out dragStart);
            dragTop = Native.Bounds(handle).Top;
            tabMoved = false; tabDragging = true;
            tab.CaptureMouse(); e.Handled = true;
        }
        private void TabMove(object sender, MouseEventArgs e)
        {
            if (!tabDragging || e.LeftButton != MouseButtonState.Pressed) return;
            Native.Point point; Native.GetCursorPos(out point);
            int delta = point.Y - dragStart.Y;
            if (Math.Abs(delta) > SystemParameters.MinimumVerticalDragDistance * Native.Scale(handle)) tabMoved = true;
            if (tabMoved) { settings.DockY = dragTop + delta; Position(); }
        }
        private void TabUp(object sender, MouseButtonEventArgs e)
        {
            if (!tabDragging) return;
            tabDragging = false; tab.ReleaseMouseCapture();
            if (tabMoved) Save(); else Restore();
            e.Handled = true;
        }
        private void SetVariant(Variant variant) { settings.Variant = variant; Update(); Save(); UpdateAnimationTimer(); }
        private void SetView(ViewMode view) { ChangeLayout(delegate { settings.View = view; }); }
        private void SetAssistants(Assistants show) { ChangeLayout(delegate { settings.Show = show; ApplyAssistants(); }); UpdateAnimationTimer(); }
        // Muestra solo las columnas elegidas y deja de consultar al asistente oculto.
        private void ApplyAssistants()
        {
            bool showClaude = settings.Show != Assistants.Codex, showCodex = settings.Show != Assistants.Claude;
            claude.Enabled = showClaude; codex.Enabled = showCodex;
            claudeColumn.Visibility = claudeDot.Visibility = showClaude ? Visibility.Visible : Visibility.Collapsed;
            codexColumn.Visibility = codexDot.Visibility = showCodex ? Visibility.Visible : Visibility.Collapsed;
            columns.ColumnDefinitions[0].Width = showClaude ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            columns.ColumnDefinitions[1].Width = showCodex ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        }
        private void ChangeLayout(Action change)
        {
            double oldWidth = PanelSize().Width;
            Native.Rect bounds = Native.Bounds(handle);
            change();
            Update();
            if (settings.DockSide == null)
            {
                // Si el panel está en la mitad derecha, conserva su borde derecho al cambiar de ancho.
                Native.Rect area = Native.WorkArea(bounds);
                if (bounds.Left + bounds.Width / 2 > (area.Left + area.Right) / 2)
                    settings.X = bounds.Left + (int)Math.Round((oldWidth - PanelSize().Width) * Native.Scale(handle));
            }
            ApplyDockContent(); Position(); Save();
        }
        private bool ClaudeActive { get { return settings.Show != Assistants.Codex && (alwaysActive || (claudeActivity != null && claudeActivity.Active)); } }
        private bool CodexActive { get { return settings.Show != Assistants.Claude && (alwaysActive || (codexActivity != null && codexActivity.Active)); } }
        private void ActivityStarted()
        {
            if (!closed) Dispatcher.BeginInvoke(new Action(UpdateAnimationTimer));
        }
        // El temporizador solo corre mientras haya una mascota visible en movimiento.
        private void UpdateAnimationTimer()
        {
            bool visible = !closed && settings.Variant == Variant.Mascots && settings.DockSide == null;
            if (visible && (claudeColumn.NeedsTicks(ClaudeActive) || codexColumn.NeedsTicks(CodexActive))) animationTimer.Start();
            else animationTimer.Stop();
        }
        private void AnimateMascots()
        {
            bool claudeActive = ClaudeActive, codexActive = CodexActive;
            if (claudeColumn.NeedsTicks(claudeActive)) claudeColumn.Animate(claudeActive);
            if (codexColumn.NeedsTicks(codexActive)) codexColumn.Animate(codexActive);
            UpdateAnimationTimer();
        }
        private void Refresh() { claude.Refresh(); codex.Refresh(); Update(); }
        private void Update()
        {
            if (closed) return;
            claudeColumn.Update(claude, settings.Variant, settings.View); codexColumn.Update(codex, settings.Variant, settings.View);
            if (ready && settings.DockSide == null && Math.Abs(PanelSize().Height - Height) > 0.5) { ApplyDockContent(); Position(); }
            logos.Background = settings.Variant == Variant.Logos ? Palette.Brush("#334B5368") : Brushes.Transparent;
            mascots.Background = settings.Variant == Variant.Mascots ? Palette.Brush("#334B5368") : Brushes.Transparent;
            claudeDot.Fill = StatusColor(claude.Reading, "#E8835F"); codexDot.Fill = StatusColor(codex.Reading, "#6C9BFF");
            claudeDot.ToolTip = "Claude Code · " + StateName(claude.Reading);
            codexDot.ToolTip = "Codex · " + StateName(codex.Reading);
        }
        private static string StateName(UsageReading reading)
        {
            switch (reading.State)
            {
                case ReadState.Ok: return reading.LastSuccessAt < DateTimeOffset.UtcNow.AddMinutes(-15) ? "Antiguo" : "Reciente";
                case ReadState.NeedsAuth: return "Sin sesión";
                case ReadState.ConnectionError: return "Sin conexión";
                case ReadState.RateLimited: return "Demasiadas consultas";
                case ReadState.ServiceError: return "Error del servicio";
                default: return "Sin datos";
            }
        }
        private static Brush StatusColor(UsageReading reading, string color)
        {
            if (reading.State == ReadState.NeedsAuth || reading.State == ReadState.ServiceError) return Palette.Brush("#FF6B6B");
            if (reading.State == ReadState.NoData) return Palette.Brush("#7C8291");
            if (reading.State != ReadState.Ok || reading.LastSuccessAt < DateTimeOffset.UtcNow.AddMinutes(-15)) return Palette.Brush("#E8BA72");
            return Palette.Brush(color);
        }
        private ContextMenu BuildMenu()
        {
            ContextMenu menu = Theme.Menu();
            menu.Items.Add(Item("Actualizar ahora", Refresh));
            var appearance = new MenuItem { Header = "Apariencia" };
            var logoItem = Item("Logotipos", delegate { SetVariant(Variant.Logos); }); logoItem.IsCheckable = true; logoItem.IsChecked = settings.Variant == Variant.Logos;
            var mascotItem = Item("Mascotas", delegate { SetVariant(Variant.Mascots); }); mascotItem.IsCheckable = true; mascotItem.IsChecked = settings.Variant == Variant.Mascots;
            appearance.Items.Add(logoItem); appearance.Items.Add(mascotItem);
            if (!settings.FixedVariant) menu.Items.Add(appearance);
            var viewMenu = new MenuItem { Header = "Vista" };
            viewMenu.Items.Add(ViewItem("Completa", ViewMode.Full));
            viewMenu.Items.Add(ViewItem("Solo imágenes", ViewMode.ImagesOnly));
            menu.Items.Add(viewMenu);
            var shown = new MenuItem { Header = "Asistentes" };
            shown.Items.Add(AssistantsItem("Ambos", Assistants.Both));
            shown.Items.Add(AssistantsItem("Solo Claude", Assistants.Claude));
            shown.Items.Add(AssistantsItem("Solo Codex", Assistants.Codex));
            menu.Items.Add(shown);
            menu.Items.Add(Item("Ocultar a la izquierda", delegate { Dock("left"); }));
            menu.Items.Add(Item("Ocultar a la derecha", delegate { Dock("right"); }));
            var startup = new MenuItem { Header = "Iniciar con Windows", IsCheckable = true, IsChecked = !testing && Installer.StartsWithWindows, IsEnabled = !testing };
            startup.Click += delegate {
                try { Installer.SetStartup(startup.IsChecked); }
                catch (Exception) { startup.IsChecked = Installer.StartsWithWindows; Native.MessageBox(handle, "No se pudo cambiar el inicio automático. Instala primero con UsageOrbit.exe --instalar y comprueba los permisos.", "Usage Orbit", 0x30); }
            };
            menu.Items.Add(startup);
            menu.Items.Add(Item("Acerca de Usage Orbit", ShowAbout));
            var uninstall = Item("Desinstalar…", delegate { Installer.Uninstall(false); }); uninstall.IsEnabled = !testing;
            menu.Items.Add(uninstall); menu.Items.Add(Item("Cerrar", Close));
            return menu;
        }
        private MenuItem ViewItem(string title, ViewMode view)
        {
            var item = Item(title, delegate { SetView(view); });
            item.IsCheckable = true; item.IsChecked = settings.View == view;
            return item;
        }
        private MenuItem AssistantsItem(string title, Assistants show)
        {
            var item = Item(title, delegate { SetAssistants(show); });
            item.IsCheckable = true; item.IsChecked = settings.Show == show;
            return item;
        }
        private static MenuItem Item(string title, Action action)
        {
            var item = new MenuItem { Header = title };
            item.Click += delegate { action(); };
            return item;
        }
        // Muestra el autor y ofrece abrir el repositorio del proyecto.
        private void ShowAbout()
        {
            const string text = "Usage Orbit\n\nCreado por LeobardoVilla88 y LeobardoVillalobos88.\nLicencia MIT.\n\n¿Abrir el repositorio en GitHub?";
            if (Native.MessageBox(handle, text, "Acerca de Usage Orbit", 0x44) == 6)
                System.Diagnostics.Process.Start("https://github.com/LeobardoVillalobos88/widget-usage-orbit");
        }
        private void OpenMenu() { ContextMenu = BuildMenu(); ContextMenu.PlacementTarget = this; ContextMenu.IsOpen = true; }
        private void Save() { if (!testing) settings.Save(); }
        protected override void OnClosed(EventArgs e)
        {
            closed = true;
            timer.Stop(); animationTimer.Stop(); claude.Dispose(); codex.Dispose();
            if (claudeActivity != null) { claudeActivity.Dispose(); codexActivity.Dispose(); }
            SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            Save(); base.OnClosed(e);
        }
    }
}
