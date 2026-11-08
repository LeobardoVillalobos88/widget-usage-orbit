using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UsageOrbit.Data;

internal static class ContractTests
{
    private static int checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        checks++;
    }
    private static int Main()
    {
        try
        {
            ParseTests();
            PollTests();
            Console.WriteLine(checks + " comprobaciones correctas. Sin UI, red, procesos auxiliares ni credenciales.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void ParseTests()
    {
        var result = CodexSource.Parse(Json.Parse(@"{""rateLimitsByLimitId"":{""codex"":{""primary"":{""usedPercent"":8,""windowDurationMins"":10080},""secondary"":{""usedPercent"":54,""windowDurationMins"":300,""resetsAt"":1793957177}}},""rateLimits"":{""primary"":{""usedPercent"":99,""windowDurationMins"":300}}}"));
        Check(result.Session.UsedPercent == 54 && result.Weekly.UsedPercent == 8, "Codex: precedencia codex y orden por duración");
        Check(CodexSource.Classify("failed to fetch codex rate limits: error sending request") == ReadState.ConnectionError, "Codex: fallo de red");
        Check(CodexSource.Classify("failed to fetch codex rate limits: 429 Too Many Requests") == ReadState.RateLimited, "Codex: límite de consultas");
        Check(CodexSource.Classify("failed to fetch codex rate limits") == ReadState.ServiceError, "Codex: otros errores");
        Check(result.Session.ResetsAt.HasValue, "Codex: época de reinicio");
        result = CodexSource.Parse(Json.Parse(@"{""rateLimits"":{""primary"":null,""secondary"":{""usedPercent"":0,""windowDurationMins"":10080}}}"));
        Check(result.Session == null && result.Weekly.UsedPercent == 0, "Codex: semana sola y cero real");
        result = CodexSource.Parse(Json.Parse(@"{""rateLimits"":{""secondary"":{""usedPercent"":24,""windowDurationMins"":120}}}"));
        Check(result.Session.UsedPercent == 24 && result.Weekly == null, "Codex: sesión sola en secondary");
        result = CodexSource.Parse(Json.Parse(@"{""rateLimits"":{""primary"":{""usedPercent"":42,""windowDurationMins"":null}}}"));
        Check(result.Session == null && result.Weekly == null && result.State == ReadState.NoData, "Codex: no inventar duración");
        result = CodexSource.Parse(Json.Parse(@"{""rateLimits"":{""primary"":{""windowDurationMins"":300}}}"));
        Check(result.Session.UsedPercent == null, "Codex: porcentaje ausente no es cero");
        result = ClaudeUsageSource.Parse(Json.Parse(@"{""five_hour"":{""utilization"":38.0,""resets_at"":""2026-11-06T09:19:59.791+00:00""},""seven_day"":null}"));
        Check(result.Session.UsedPercent == 38 && result.Session.WindowMinutes == 300 && result.Weekly == null, "Claude: porcentaje sin conversión");
        Check(result.Session.ResetsAt.Value.Offset == TimeSpan.Zero, "Claude: zona de reinicio");
        result = ClaudeUsageSource.Parse(Json.Parse(@"{""seven_day"":{""utilization"":0}}"));
        Check(result.Session == null && result.Weekly.UsedPercent == 0 && result.Weekly.WindowMinutes == 10080, "Claude: semana sola y cero real");
        result = ClaudeUsageSource.Parse(Json.Parse(@"{""five_hour"":{}}"));
        Check(result.Session.UsedPercent == null && result.State == ReadState.NoData, "Claude: porcentaje ausente");
        result = ClaudeUsageSource.Parse(Json.Parse("{}"));
        Check(result.State == ReadState.NoData, "Claude: ambas ventanas ausentes");
        bool rejected = false;
        try { ClaudeUsageSource.Parse(Json.Parse(@"{""error"":""unexpected""}")); } catch (FormatException) { rejected = true; }
        Check(rejected, "Claude: respuesta inesperada");
        Check(Json.Epoch(double.MaxValue, false) == null && Json.Minutes(Json.Parse(@"{""n"":0}"), "n") == null, "Datos fuera de rango");
        foreach (string scenario in new[] { "reciente", "antiguo", "sinconexion", "sesion", "sindatos", "vencido", "limite", "mascotas" })
            Check(TestData.IsScenario(scenario) && TestData.Create(scenario, true) != null, "Escenario " + scenario);
    }
    private static void PollTests()
    {
        var context = new Pump();
        SynchronizationContext.SetSynchronizationContext(context);
        var source = new FakeSource();
        using (var poller = new UsagePoller(source, null))
        {
            poller.Tick(); poller.Tick(); poller.Refresh();
            Check(source.Started.Wait(2000) && source.Calls == 1 && poller.Busy, "Sondeo: nunca simultáneo");
            var valid = UsageReading.Success(new UsageWindow { UsedPercent = 54, WindowMinutes = 300 }, null);
            source.Complete(valid); context.Until(() => !poller.Busy);
            Check(poller.Reading.Session.UsedPercent == 54 && poller.NextAttempt > DateTimeOffset.UtcNow.AddMinutes(4), "Sondeo: intervalo normal");
            source.Reset(); poller.Refresh();
            Check(source.Started.Wait(2000), "Sondeo: manual cancela espera");
            source.Complete(UsageReading.Failure(ReadState.ConnectionError, "Sin conexión")); context.Until(() => !poller.Busy);
            Check(poller.Reading.Session.UsedPercent == 54 && poller.Reading.LastSuccessAt == valid.LastSuccessAt, "Sondeo: conservar última lectura");
            Check(poller.NextAttempt > DateTimeOffset.UtcNow.AddSeconds(50) && poller.NextAttempt < DateTimeOffset.UtcNow.AddSeconds(65), "Sondeo: primer reintento un minuto");
            source.Reset(); poller.Refresh(); Check(source.Started.Wait(2000), "Sondeo: segundo intento");
            source.Complete(UsageReading.Failure(ReadState.ServiceError, "Error")); context.Until(() => !poller.Busy);
            Check(poller.NextAttempt > DateTimeOffset.UtcNow.AddSeconds(110), "Sondeo: espera progresiva");
            source.Reset(); poller.Refresh(); Check(source.Started.Wait(2000), "Sondeo: consulta limitada");
            var limit = UsageReading.Failure(ReadState.RateLimited, "Límite"); limit.RetryAfter = DateTimeOffset.UtcNow.AddMinutes(20);
            source.Complete(limit); context.Until(() => !poller.Busy);
            int calls = source.Calls; poller.Refresh(); poller.Tick();
            Check(source.Calls == calls && !poller.Busy && poller.Notice != null, "Sondeo: manual respeta Retry-After");
            Check(poller.NextAttempt >= limit.RetryAfter, "Sondeo: no programar antes de Retry-After");
        }
        source = new FakeSource();
        var expired = UsageReading.Success(new UsageWindow { UsedPercent = 50, WindowMinutes = 300, ResetsAt = DateTimeOffset.UtcNow.AddSeconds(-1) }, null);
        using (var poller = new UsagePoller(source, null))
        {
            poller.Tick(); Check(source.Started.Wait(2000), "Reinicio: primera consulta");
            source.Complete(expired); context.Until(() => !poller.Busy);
            source.Reset(); poller.Tick(); Check(source.Started.Wait(2000), "Reinicio: consulta inmediata pese al intervalo normal");
            source.Complete(expired); context.Until(() => !poller.Busy);
            int calls = source.Calls; poller.Tick();
            Check(source.Calls == calls, "Reinicio: no repetir continuamente");
        }
        using (var poller = new UsagePoller(null, TestData.Create("reciente", true)))
        {
            poller.Refresh(); poller.Tick(); Check(!poller.Busy, "Modo de prueba: sin fuente");
        }
        SynchronizationContext.SetSynchronizationContext(null);
    }
    private sealed class FakeSource : IUsageSource
    {
        private TaskCompletionSource<UsageReading> completion = new TaskCompletionSource<UsageReading>();
        public readonly ManualResetEventSlim Started = new ManualResetEventSlim();
        public int Calls;
        public Task<UsageReading> ReadAsync(CancellationToken cancellation) { Interlocked.Increment(ref Calls); Started.Set(); return completion.Task; }
        public void Complete(UsageReading value) { completion.SetResult(value); }
        public void Reset() { completion = new TaskCompletionSource<UsageReading>(); Started.Reset(); }
    }
    private sealed class Pump : SynchronizationContext
    {
        private readonly BlockingCollection<Action> queue = new BlockingCollection<Action>();
        public override void Post(SendOrPostCallback callback, object state) { queue.Add(() => callback(state)); }
        public void Until(Func<bool> condition)
        {
            while (!condition())
            {
                Action action;
                if (!queue.TryTake(out action, 2000)) throw new Exception("Tiempo agotado esperando el sondeo de prueba.");
                action();
            }
        }
    }
}
