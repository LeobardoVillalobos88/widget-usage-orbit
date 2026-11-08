using System;
using System.Threading;
using System.Threading.Tasks;

namespace UsageOrbit.Data
{
    public sealed class UsagePoller : IDisposable
    {
        private readonly IUsageSource source;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private int failures;
        private DateTimeOffset lastResetQuery = DateTimeOffset.MinValue;
        private DateTimeOffset? requestedSessionReset, requestedWeeklyReset;
        private bool disposed;
        public UsageReading Reading { get; private set; }
        public DateTimeOffset NextAttempt { get; private set; }
        public bool Busy { get; private set; }
        public string Notice { get; private set; }
        public event Action Changed;
        // Un asistente oculto no se consulta.
        public bool Enabled { get; set; }

        public UsagePoller(IUsageSource source, UsageReading fixture)
        {
            this.source = source;
            Enabled = true;
            Reading = fixture ?? UsageReading.Failure(ReadState.NoData, "Esperando la primera lectura.");
            NextAttempt = source == null ? DateTimeOffset.UtcNow.AddMinutes(4) : DateTimeOffset.UtcNow;
        }
        public void Tick()
        {
            if (source == null || !Enabled || Busy || disposed) return;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset? sessionReset = Expired(Reading.Session, now), weeklyReset = Expired(Reading.Weekly, now);
            bool resetDue = ((sessionReset.HasValue && sessionReset != requestedSessionReset) ||
                (weeklyReset.HasValue && weeklyReset != requestedWeeklyReset)) && now - lastResetQuery >= TimeSpan.FromMinutes(1);
            if (now >= NextAttempt || resetDue)
            {
                if (Reading.RetryAfter.HasValue && Reading.RetryAfter > now) return;
                if (resetDue) { requestedSessionReset = sessionReset; requestedWeeklyReset = weeklyReset; lastResetQuery = now; }
                StartRead();
            }
        }
        private static DateTimeOffset? Expired(UsageWindow window, DateTimeOffset now)
        {
            return window != null && window.ResetsAt <= now ? window.ResetsAt : null;
        }
        public void Refresh()
        {
            if (disposed || source == null || !Enabled) return;
            if (Reading.RetryAfter > DateTimeOffset.UtcNow)
            {
                Notice = "Espera el plazo indicado antes de actualizar.";
                Notify();
                return;
            }
            if (!Busy) StartRead();
        }
        private async void StartRead()
        {
            Busy = true;
            Notice = "Actualizando…";
            Notify();
            try
            {
                UsageReading result;
                try { result = await Task.Run(() => source.ReadAsync(cancellation.Token)); }
                catch (OperationCanceledException) { return; }
                catch (Exception) { result = UsageReading.Failure(ReadState.ServiceError, "No se pudo completar la consulta."); }
                if (disposed) return;
                DateTimeOffset now = DateTimeOffset.UtcNow;
                if (result.State == ReadState.Ok || result.State == ReadState.NoData)
                {
                    failures = 0;
                    NextAttempt = now.AddMinutes(5);
                }
                else
                {
                    result.Session = Reading.Session;
                    result.Weekly = Reading.Weekly;
                    result.LastSuccessAt = Reading.LastSuccessAt;
                    NextAttempt = now.AddMinutes(Math.Min(30, Math.Pow(2, Math.Min(failures++, 5))));
                }
                if (result.RetryAfter > NextAttempt) NextAttempt = result.RetryAfter.Value;
                Reading = result;
            }
            finally
            {
                Busy = false;
                Notice = null;
                if (!disposed) Notify();
            }
        }
        private void Notify() { if (Changed != null) Changed(); }
        public void Dispose() { disposed = true; cancellation.Cancel(); cancellation.Dispose(); }
    }
}
