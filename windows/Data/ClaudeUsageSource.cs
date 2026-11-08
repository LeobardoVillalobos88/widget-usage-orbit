// Este endpoint no está documentado públicamente; lo usa la propia CLI para /usage.
// Este archivo es el único punto que debe repararse si cambia ese contrato.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using UsageOrbit.Platform;

namespace UsageOrbit.Data
{
    public sealed class ClaudeUsageSource : IUsageSource
    {
        public async Task<UsageReading> ReadAsync(CancellationToken cancellation)
        {
            try
            {
                string directory = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
                if (string.IsNullOrWhiteSpace(directory)) directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
                string path = Path.Combine(directory, ".credentials.json");
                DateTimeOffset? expires;
                string token = ReadToken(path, out expires);
                if (string.IsNullOrWhiteSpace(token)) return Login(false);
                if (expires.HasValue && expires <= DateTimeOffset.UtcNow.AddMinutes(1))
                {
                    token = null;
                    await Refresh(cancellation).ConfigureAwait(false);
                    token = ReadToken(path, out expires);
                    if (string.IsNullOrWhiteSpace(token) || (expires.HasValue && expires <= DateTimeOffset.UtcNow)) return Login(true);
                }
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                using (var handler = new HttpClientHandler { AllowAutoRedirect = false })
                using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) })
                using (var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage"))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    token = null;
                    request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
                    request.Headers.UserAgent.ParseAdd("UsageOrbit/1.0");
                    using (var response = await client.SendAsync(request, cancellation).ConfigureAwait(false))
                    {
                        int status = (int)response.StatusCode;
                        if (status == 401 || status == 403) return Login(false);
                        if (!response.IsSuccessStatusCode)
                        {
                            var result = status == 429 ? UsageReading.Failure(ReadState.RateLimited, "Demasiadas consultas.") :
                                UsageReading.Failure(ReadState.ServiceError, "Claude devolvió HTTP " + status + ".");
                            var retry = response.Headers.RetryAfter;
                            if (retry != null)
                                result.RetryAfter = retry.Date ?? (retry.Delta.HasValue ? (DateTimeOffset?)DateTimeOffset.UtcNow.Add(retry.Delta.Value) : null);
                            return result;
                        }
                        return Parse(Json.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false)));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                cancellation.ThrowIfCancellationRequested();
                return UsageReading.Failure(ReadState.ConnectionError, "Claude no respondió a tiempo.");
            }
            catch (HttpRequestException) { return UsageReading.Failure(ReadState.ConnectionError, "No se pudo conectar con Claude."); }
            catch (Exception) { return UsageReading.Failure(ReadState.ServiceError, "No se pudo leer el consumo de Claude. Comprueba la CLI y sus permisos."); }
        }

        private static UsageReading Login(bool expired)
        {
            return UsageReading.Failure(ReadState.NeedsAuth, (expired ? "Sesión caducada. " : "") + "Ejecuta: claude auth login");
        }
        private static string ReadToken(string path, out DateTimeOffset? expires)
        {
            expires = null;
            if (!File.Exists(path)) return null;
            var oauth = Json.Object(Json.Parse(File.ReadAllText(path)), "claudeAiOauth");
            expires = Json.Epoch(Json.Number(oauth, "expiresAt"), true);
            return Json.Get(oauth, "accessToken") as string;
        }
        // La CLI renueva su token al ejecutar este comando local, sin inferencia, y ella misma actualiza el archivo.
        private static async Task Refresh(CancellationToken cancellation)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                try
                {
                    string executable, arguments;
                    ChildProcess.Resolve("claude", "plugin list", out executable, out arguments);
                    using (var child = new ChildProcess(executable, arguments))
                    {
                        child.Input.Dispose();
                        // Se descarta la salida: puede contener información de la cuenta.
                        while (await ChildProcess.Within(child.Output.ReadLineAsync(), timeout.Token).ConfigureAwait(false) != null) { }
                    }
                }
                catch (OperationCanceledException) { cancellation.ThrowIfCancellationRequested(); }
            }
        }
        internal static UsageReading Parse(Dictionary<string, object> data)
        {
            if (data.Count > 0 && !data.ContainsKey("five_hour") && !data.ContainsKey("seven_day")) throw new FormatException();
            return UsageReading.Success(Window(data, "five_hour", 300), Window(data, "seven_day", 10080));
        }
        private static UsageWindow Window(Dictionary<string, object> data, string key, int minutes)
        {
            object raw = Json.Get(data, key);
            if (raw == null) return null;
            var value = raw as Dictionary<string, object>;
            if (value == null) throw new FormatException();
            DateTimeOffset reset;
            string text = Json.Get(value, "resets_at") as string;
            return new UsageWindow {
                UsedPercent = Json.Number(value, "utilization"), WindowMinutes = minutes,
                ResetsAt = DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out reset) ? (DateTimeOffset?)reset : null
            };
        }
    }
}
