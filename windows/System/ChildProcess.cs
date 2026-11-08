using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace UsageOrbit.Platform
{
    internal sealed class ChildProcess : IDisposable
    {
        private readonly JobObject job = new JobObject();
        private static readonly object startLock = new object();
        private StreamReader error;
        public StreamWriter Input { get; private set; }
        public StreamReader Output { get; private set; }

        public ChildProcess(string executable, string arguments)
        {
            try
            {
                // El hijo no ejecuta código hasta quedar asociado al Job Object.
                lock (startLock) StartSuspended(executable, arguments);
                ObserveFailure(DrainError());
            }
            catch { Dispose(); throw; }
        }

        private void StartSuspended(string executable, string arguments)
        {
            IntPtr inputRead = IntPtr.Zero, inputWrite = IntPtr.Zero, outputRead = IntPtr.Zero;
            IntPtr outputWrite = IntPtr.Zero, errorRead = IntPtr.Zero, errorWrite = IntPtr.Zero;
            var process = new ProcessInformation();
            try
            {
                Pipe(out inputRead, out inputWrite, false);
                Pipe(out outputRead, out outputWrite, true);
                Pipe(out errorRead, out errorWrite, true);
                var startup = new StartupInfo { Size = Marshal.SizeOf(typeof(StartupInfo)), Flags = 0x100,
                    Input = inputRead, Output = outputWrite, Error = errorWrite };
                if (!CreateProcess(executable, new StringBuilder("\"" + executable + "\" " + arguments), IntPtr.Zero, IntPtr.Zero,
                    true, 0x08000004, IntPtr.Zero, null, ref startup, out process)) throw new Win32Exception();
                job.Assign(process.Process);
                Input = new StreamWriter(ParentStream(ref inputWrite, FileAccess.Write), new UTF8Encoding(false)) { AutoFlush = true };
                Output = new StreamReader(ParentStream(ref outputRead, FileAccess.Read), Encoding.UTF8);
                error = new StreamReader(ParentStream(ref errorRead, FileAccess.Read), Encoding.UTF8);
                if (ResumeThread(process.Thread) == uint.MaxValue) throw new Win32Exception();
            }
            catch
            {
                if (process.Process != IntPtr.Zero) TerminateProcess(process.Process, 1);
                throw;
            }
            finally
            {
                foreach (IntPtr handle in new[] { inputRead, inputWrite, outputRead, outputWrite, errorRead, errorWrite, process.Thread, process.Process })
                    if (handle != IntPtr.Zero) CloseHandle(handle);
            }
        }
        private static void Pipe(out IntPtr read, out IntPtr write, bool parentReads)
        {
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf(typeof(SecurityAttributes)), InheritHandle = true };
            if (!CreatePipe(out read, out write, ref attributes, 0)) throw new Win32Exception();
            if (!SetHandleInformation(parentReads ? read : write, 1, 0)) throw new Win32Exception();
        }
        private static FileStream ParentStream(ref IntPtr handle, FileAccess access)
        {
            var safe = new SafeFileHandle(handle, true);
            handle = IntPtr.Zero;
            try { return new FileStream(safe, access, 4096, false); }
            catch { safe.Dispose(); throw; }
        }
        private async Task DrainError()
        {
            var buffer = new char[2048];
            while (await error.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false) != 0) { }
        }

        // Busca la CLI instalada con npm (.cmd) o como ejecutable nativo (.exe) y prepara cómo lanzarla.
        public static void Resolve(string name, string arguments, out string executable, out string commandLine)
        {
            string found = Find(name + ".cmd") ?? Find(name + ".exe");
            if (found == null) throw new FileNotFoundException("No se encontró la CLI.");
            if (found.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
            {
                executable = Path.Combine(Environment.SystemDirectory, "cmd.exe");
                commandLine = CmdArguments(found, arguments);
            }
            else
            {
                executable = found;
                commandLine = arguments;
            }
        }
        private static string Find(string fileName)
        {
            var folders = new System.Collections.Generic.List<string> {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".local\bin")
            };
            folders.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'));
            foreach (string folder in folders)
            {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                string candidate = Path.Combine(folder.Trim().Trim('"'), fileName);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        public static string CmdArguments(string path, string arguments)
        {
            // cmd interpreta estas rutas como código, por lo que no se aceptan expansiones.
            if (path.IndexOfAny(new[] { '"', '%', '\r', '\n' }) >= 0) throw new IOException("Ruta de CLI no válida.");
            return "/d /v:off /s /c \"\"" + path + "\" " + arguments + "\"";
        }

        public static async Task<T> Within<T>(Task<T> operation, CancellationToken cancellation)
        {
            var stopped = new TaskCompletionSource<bool>();
            using (cancellation.Register(delegate { stopped.TrySetResult(true); }))
            {
                if (await Task.WhenAny(operation, stopped.Task).ConfigureAwait(false) != operation)
                {
                    // Observa también los errores que lleguen al cerrar el proceso cancelado.
                    ObserveFailure(operation);
                    cancellation.ThrowIfCancellationRequested();
                }
                return await operation.ConfigureAwait(false);
            }
        }

        private static void ObserveFailure(Task operation)
        {
            operation.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }

        public void Dispose()
        {
            job.Dispose();
            if (Input != null) { try { Input.Dispose(); } catch (IOException) { } }
            if (Output != null) Output.Dispose();
            if (error != null) error.Dispose();
        }

        [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes
        {
            public int Length;
            public IntPtr Descriptor;
            [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo
        {
            public int Size;
            public string Reserved, Desktop, Title;
            public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
            public short ShowWindow, ReservedSize;
            public IntPtr ReservedPointer, Input, Output, Error;
        }
        [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation
        {
            public IntPtr Process, Thread;
            public uint ProcessId, ThreadId;
        }
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CreatePipe(out IntPtr read, out IntPtr write, ref SecurityAttributes attributes, uint size);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation process);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll")] private static extern bool TerminateProcess(IntPtr process, uint code);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
}
