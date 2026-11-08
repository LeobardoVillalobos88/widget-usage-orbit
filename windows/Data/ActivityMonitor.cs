using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace UsageOrbit.Data
{
    // Detecta que un asistente está trabajando porque escribe en sus registros de sesión.
    // Solo observa cuándo cambian los archivos; nunca lee su contenido.
    public sealed class ActivityMonitor : IDisposable
    {
        // Se mide con un reloj monotónico para no depender de cambios en la hora del sistema.
        private static readonly long ActiveWindow = Stopwatch.Frequency * 90;
        private readonly string folder;
        private FileSystemWatcher watcher;
        private long lastWrite = Stopwatch.GetTimestamp() - ActiveWindow;
        public event Action Started;

        public ActivityMonitor(string folder)
        {
            this.folder = folder;
            Retry();
        }
        // La carpeta de sesiones puede crearse después de abrir el widget, con la primera conversación.
        public void Retry()
        {
            if (watcher != null || !Directory.Exists(folder)) return;
            watcher = new FileSystemWatcher(folder, "*.jsonl") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
            watcher.Changed += OnWrite;
            watcher.Created += OnWrite;
            watcher.EnableRaisingEvents = true;
        }
        public bool Active
        {
            get { return Stopwatch.GetTimestamp() - Interlocked.Read(ref lastWrite) < ActiveWindow; }
        }
        private void OnWrite(object sender, FileSystemEventArgs e)
        {
            bool wasActive = Active;
            Interlocked.Exchange(ref lastWrite, Stopwatch.GetTimestamp());
            if (!wasActive && Started != null) Started();
        }
        public void Dispose() { if (watcher != null) watcher.Dispose(); }
    }
}
