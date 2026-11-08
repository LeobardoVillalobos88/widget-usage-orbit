using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace UsageOrbit.Platform
{
    internal static class Installer
    {
        private static string InstallPath { get { return Settings.DirectoryPath; } }
        private static string Executable { get { return Path.Combine(InstallPath, "UsageOrbit.exe"); } }
        private static string StartupLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Usage Orbit (inicio).lnk"); } }
        private static string MenuFolder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Usage Orbit"); } }
        private static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Usage Orbit.lnk"); } }
        public static bool StartsWithWindows { get { return File.Exists(StartupLink); } }
        public static void SetStartup(bool enabled)
        {
            if (enabled)
            {
                if (!File.Exists(Executable)) throw new InvalidOperationException("Ejecuta UsageOrbit.exe --instalar para activar el inicio con Windows.");
                Shortcut(StartupLink, "--inicio-diferido");
            }
            else if (File.Exists(StartupLink)) File.Delete(StartupLink);
        }
        public static int Install()
        {
            try
            {
                string current = Assembly.GetExecutingAssembly().Location;
                string directory = Path.GetDirectoryName(current);
                string source = Directory.Exists(Path.Combine(directory, "src")) ? Path.Combine(directory, "src") :
                    (string.Equals(Path.GetFileName(directory), "bin", StringComparison.OrdinalIgnoreCase) ? Directory.GetParent(directory).FullName : directory);
                if (!File.Exists(Path.Combine(source, "build.cmd"))) throw new IOException("No se encontró la carpeta de fuentes junto al ejecutable.");
                Directory.CreateDirectory(InstallPath);
                CopyFile(current, Executable);
                CopyFile(current + ".config", Executable + ".config");
                // Las fuentes se reemplazan completas para que no queden archivos que ya no existen en el origen.
                string sources = Path.Combine(InstallPath, "src");
                if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(sources), StringComparison.OrdinalIgnoreCase) && Directory.Exists(sources))
                    Directory.Delete(sources, true);
                CopySources(source, sources);
                Directory.CreateDirectory(MenuFolder);
                Shortcut(DesktopLink, "");
                Shortcut(Path.Combine(MenuFolder, "Usage Orbit.lnk"), "");
                Shortcut(Path.Combine(MenuFolder, "Desinstalar Usage Orbit.lnk"), "--desinstalar");
                SetStartup(true);
                string log = Path.Combine(InstallPath, "install.log");
                if (File.Exists(log)) File.Delete(log);
                return 0;
            }
            catch (Exception ex)
            {
                try { Directory.CreateDirectory(InstallPath); File.WriteAllText(Path.Combine(InstallPath, "install.log"), "No se pudo instalar Usage Orbit. " + ex.GetType().Name + ": " + ex.Message); } catch (Exception) { }
                return 1;
            }
        }
        private static void CopyFile(string source, string target)
        {
            if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) File.Copy(source, target, true);
        }
        private static void CopySources(string source, string target)
        {
            if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) return;
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
            {
                string extension = Path.GetExtension(file);
                if (extension == ".exe" || extension == ".pdb" || extension == ".log") continue;
                CopyFile(file, Path.Combine(target, Path.GetFileName(file)));
            }
            foreach (string folder in Directory.GetDirectories(source))
            {
                string name = Path.GetFileName(folder);
                if (name.Equals("bin", StringComparison.OrdinalIgnoreCase) || name.StartsWith(".") || (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;
                CopySources(folder, Path.Combine(target, name));
            }
        }
        private static void Shortcut(string path, string arguments)
        {
            object shell = null, shortcut = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
                shortcut = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                Type type = shortcut.GetType();
                type.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { Executable });
                type.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { arguments });
                type.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { InstallPath });
                type.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { Executable + ",0" });
                type.InvokeMember("WindowStyle", BindingFlags.SetProperty, null, shortcut, new object[] { 7 });
                type.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
                if (shell != null) Marshal.FinalReleaseComObject(shell);
            }
        }
        public static int Uninstall(bool silent)
        {
            if (!silent && Native.MessageBox(IntPtr.Zero, "¿Desinstalar Usage Orbit y eliminar su configuración? No se borrarán los datos de Claude ni de Codex.", "Desinstalar Usage Orbit", 0x24) != 6) return 0;
            try
            {
                string path = Path.GetFullPath(InstallPath).TrimEnd(Path.DirectorySeparatorChar);
                string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageOrbit");
                if (!path.EndsWith(@"\UsageOrbit", StringComparison.OrdinalIgnoreCase) || !string.Equals(path, expected, StringComparison.OrdinalIgnoreCase)) return 1;
                if (path.IndexOfAny(new[] { '"', '%', '\r', '\n' }) >= 0) return 1;
                Program.Signal(Program.ExitEventName);
                Program.Signal(Program.CancelDelayEventName);
                SetStartup(false);
                foreach (string link in new[] { DesktopLink, Path.Combine(MenuFolder, "Usage Orbit.lnk"), Path.Combine(MenuFolder, "Desinstalar Usage Orbit.lnk") })
                    if (File.Exists(link)) File.Delete(link);
                if (Directory.Exists(MenuFolder) && Directory.GetFileSystemEntries(MenuFolder).Length == 0) Directory.Delete(MenuFolder);
                string sleeper = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
                Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    "/d /v:off /s /c \"\"" + sleeper + "\" -NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 4\" & rmdir /s /q \"" + path + "\"\"") {
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
                }).Dispose();
                return 0;
            }
            catch (Exception) { return 1; }
        }
    }
}
