using System;

namespace UsageOrbit.Data
{
    internal static class TestData
    {
        public static bool IsScenario(string value)
        {
            return Array.IndexOf(new[] { "reciente", "antiguo", "sinconexion", "sesion", "sindatos", "vencido", "limite", "mascotas", "animacion", "soloclaude", "solocodex" }, value) >= 0;
        }
        public static UsageReading Create(string scenario, bool claude)
        {
            var now = DateTimeOffset.UtcNow;
            var reading = UsageReading.Success(
                new UsageWindow { UsedPercent = claude ? 38 : 54, WindowMinutes = 300, ResetsAt = now.AddMinutes(80) },
                new UsageWindow { UsedPercent = claude ? 21 : 8, WindowMinutes = 10080, ResetsAt = now.AddHours(52) });
            reading.LastSuccessAt = now.AddMinutes(-2);
            if (scenario == "antiguo") reading.LastSuccessAt = now.AddMinutes(-40);
            if (scenario == "sinconexion") { reading.State = ReadState.ConnectionError; reading.Message = "Sin conexión de prueba."; }
            if (scenario == "sesion") { reading.State = ReadState.NeedsAuth; reading.Message = "Ejecuta: " + (claude ? "claude auth login" : "codex login"); }
            if (scenario == "sindatos") { reading.Session = null; reading.Weekly = null; reading.State = ReadState.NoData; }
            if (scenario == "vencido") reading.Session.ResetsAt = now.AddMinutes(-1);
            if (scenario == "limite") { reading.State = ReadState.RateLimited; reading.RetryAfter = now.AddMinutes(8); }
            return reading;
        }
    }
}
