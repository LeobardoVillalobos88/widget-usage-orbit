using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UsageOrbit.Platform;

namespace UsageOrbit.Data
{
    public sealed class CodexSource : IUsageSource
    {
        public async Task<UsageReading> ReadAsync(CancellationToken cancellation)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(25));
                try
                {
                    string npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm");
                    string exe = Path.Combine(npm, @"node_modules\@openai\codex\node_modules\@openai\codex-win32-x64\vendor\x86_64-pc-windows-msvc\bin\codex.exe");
                    string args = "app-server";
                    if (!File.Exists(exe))
                    {
                        try { ChildProcess.Resolve("codex", "app-server", out exe, out args); }
                        catch (FileNotFoundException) { return UsageReading.Failure(ReadState.ServiceError, "No se encontró Codex CLI."); }
                    }
                    using (var child = new ChildProcess(exe, args))
                    {
                        await Send(child, "{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"usage_orbit\",\"title\":\"Usage Orbit\",\"version\":\"1.0.0\"}}}", timeout.Token).ConfigureAwait(false);
                        await Receive(child, 1, timeout.Token).ConfigureAwait(false);
                        await Send(child, "{\"method\":\"initialized\"}", timeout.Token).ConfigureAwait(false);
                        await Send(child, "{\"id\":2,\"method\":\"account/read\",\"params\":{\"refreshToken\":false}}", timeout.Token).ConfigureAwait(false);
                        var account = await Receive(child, 2, timeout.Token).ConfigureAwait(false);
                        if (Json.Get(account, "account") == null && object.Equals(Json.Get(account, "requiresOpenaiAuth"), true))
                            return UsageReading.Failure(ReadState.NeedsAuth, "Ejecuta: codex login");
                        await Send(child, "{\"id\":3,\"method\":\"account/rateLimits/read\"}", timeout.Token).ConfigureAwait(false);
                        return Parse(await Receive(child, 3, timeout.Token).ConfigureAwait(false));
                    }
                }
                catch (OperationCanceledException)
                {
                    cancellation.ThrowIfCancellationRequested();
                    return UsageReading.Failure(ReadState.ConnectionError, "Codex no respondió en 25 s.");
                }
                catch (CodexErrorException error) { return UsageReading.Failure(Classify(error.Message), "Codex informó un error al consultar sus límites."); }
                catch (Exception) { return UsageReading.Failure(ReadState.ServiceError, "No se pudo consultar Codex. Comprueba la instalación y los permisos de la CLI."); }
            }
        }

        private static async Task Send(ChildProcess child, string line, CancellationToken cancellation)
        {
            await ChildProcess.Within(Write(child, line), cancellation).ConfigureAwait(false);
        }
        private static async Task<bool> Write(ChildProcess child, string line)
        {
            await child.Input.WriteLineAsync(line).ConfigureAwait(false);
            return true;
        }
        private static async Task<Dictionary<string, object>> Receive(ChildProcess child, int id, CancellationToken cancellation)
        {
            while (true)
            {
                string line = await ChildProcess.Within(child.Output.ReadLineAsync(), cancellation).ConfigureAwait(false);
                if (line == null) throw new IOException("La CLI cerró su salida.");
                var message = Json.Parse(line);
                if (Json.Number(message, "id") != id) continue;
                var error = Json.Object(message, "error");
                if (error != null) throw new CodexErrorException(Json.Get(error, "message") as string ?? "");
                var result = Json.Object(message, "result");
                if (result == null) throw new FormatException();
                return result;
            }
        }

        // El servidor solo describe estos fallos en el texto del error. "rate limit" no sirve
        // como señal porque también forma parte del nombre de la consulta.
        internal static ReadState Classify(string message)
        {
            string text = message.ToLowerInvariant();
            if (text.Contains("429") || text.Contains("too many requests")) return ReadState.RateLimited;
            if (text.Contains("error sending request") || text.Contains("connect") || text.Contains("timed out") || text.Contains("dns")) return ReadState.ConnectionError;
            return ReadState.ServiceError;
        }

        private sealed class CodexErrorException : Exception
        {
            public CodexErrorException(string message) : base(message) { }
        }

        internal static UsageReading Parse(Dictionary<string, object> result)
        {
            var limits = Json.Object(Json.Object(result, "rateLimitsByLimitId"), "codex") ?? Json.Object(result, "rateLimits");
            if (limits == null) throw new FormatException();
            UsageWindow session = null, weekly = null;
            foreach (string key in new[] { "primary", "secondary" })
            {
                var item = Json.Object(limits, key);
                if (item == null) continue;
                var window = new UsageWindow {
                    UsedPercent = Json.Number(item, "usedPercent"), WindowMinutes = Json.Minutes(item, "windowDurationMins"),
                    ResetsAt = Json.Epoch(Json.Number(item, "resetsAt"), false)
                };
                // Sin duración no hay información suficiente para asignar el periodo.
                if (!window.WindowMinutes.HasValue) continue;
                if (window.WindowMinutes >= 10080)
                {
                    if (weekly == null || window.WindowMinutes < weekly.WindowMinutes) weekly = window;
                }
                else if (session == null || window.WindowMinutes < session.WindowMinutes) session = window;
            }
            return UsageReading.Success(session, weekly);
        }
    }
}
