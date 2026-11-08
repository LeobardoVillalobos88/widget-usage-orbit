using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using UsageOrbit.Platform;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
[assembly: System.Reflection.AssemblyTitle("Usage Orbit")]
[assembly: System.Reflection.AssemblyDescription("Widget de consumo de Claude Code y Codex")]
[assembly: System.Reflection.AssemblyProduct("Usage Orbit")]
[assembly: System.Reflection.AssemblyCompany("LeobardoVilla88 y LeobardoVillalobos88")]
[assembly: System.Reflection.AssemblyCopyright("Copyright © 2026 LeobardoVilla88 y LeobardoVillalobos88")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace UsageOrbit
{
    internal static class Program
    {
        internal const string RestoreEventName = @"Local\UsageOrbit.Restore.v1";
        internal const string ExitEventName = @"Local\UsageOrbit.Exit.v1";
        internal const string CancelDelayEventName = @"Local\UsageOrbit.CancelDelay.v1";

        [STAThread]
        private static int Main(string[] args)
        {
            bool delayed = false, silent = false, install = false, uninstall = false;
            string scenario = null;
            int? x = null, y = null;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--inicio-diferido": delayed = true; break;
                    case "--silencioso": silent = true; break;
                    case "--instalar": install = true; break;
                    case "--desinstalar": uninstall = true; break;
                    case "--prueba": if (++i >= args.Length) return 2; scenario = args[i]; break;
                    case "--x": case "--y":
                        string axis = args[i];
                        int coordinate;
                        if (++i >= args.Length || !int.TryParse(args[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out coordinate) || coordinate < -1000000 || coordinate > 1000000) return 2;
                        if (axis == "--x") x = coordinate; else y = coordinate;
                        break;
                    default: return 2;
                }
            }
            if (scenario != null && (!Data.TestData.IsScenario(scenario) || install || uninstall || delayed)) return 2;
            if (install && uninstall) return 2;
            if ((x.HasValue || y.HasValue) && scenario == null) return 2;
            if (install) return Installer.Install();
            if (uninstall) return Installer.Uninstall(silent);
            if (delayed)
            {
                // La espera termina antes si el usuario ya abrió el widget o lo está desinstalando.
                using (var cancel = new EventWaitHandle(false, EventResetMode.ManualReset, CancelDelayEventName))
                    if (cancel.WaitOne(TimeSpan.FromSeconds(180))) return 0;
            }
            string suffix = scenario == null ? "" : ".Test";
            bool created;
            using (var restore = new EventWaitHandle(false, EventResetMode.AutoReset, RestoreEventName + suffix))
            using (var quit = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName + suffix))
            using (var mutex = new Mutex(true, @"Local\UsageOrbit.Widget.v1" + suffix, out created))
            {
                if (!created) { if (!delayed) restore.Set(); return 0; }
                if (scenario == null) Signal(CancelDelayEventName);
                try { return RunWidget(scenario, x, y, restore, quit); }
                finally { mutex.ReleaseMutex(); }
            }
        }
        internal static void Signal(string name)
        {
            try { using (var signal = EventWaitHandle.OpenExisting(name)) signal.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int RunWidget(string scenario, int? x, int? y, EventWaitHandle restore, EventWaitHandle quit)
        {
            var app = new System.Windows.Application();
            app.ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
            app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
            {
                if (scenario == null) Settings.Log("Error de interfaz: " + e.Exception.GetType().Name);
                e.Handled = true;
            };
            var window = new App.WidgetWindow(scenario, x, y);
            var restoreWait = ThreadPool.RegisterWaitForSingleObject(restore, delegate {
                if (!app.Dispatcher.HasShutdownStarted) app.Dispatcher.BeginInvoke(new Action(window.RestoreAndActivate));
            }, null, Timeout.Infinite, false);
            var exitWait = ThreadPool.RegisterWaitForSingleObject(quit, delegate {
                if (!app.Dispatcher.HasShutdownStarted) app.Dispatcher.BeginInvoke(new Action(window.Close));
            }, null, Timeout.Infinite, false);
            try { return app.Run(window); }
            finally { restoreWait.Unregister(null); exitWait.Unregister(null); }
        }
    }
}
