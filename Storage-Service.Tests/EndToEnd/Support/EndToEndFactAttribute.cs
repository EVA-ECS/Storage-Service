using Xunit;

namespace Storage_Service.Tests.EndToEnd.Support;

// Ohne ausdrücklichen Start bleiben die schnellen Unit-Tests unabhängig von Docker und Supabase.
public sealed class EndToEndFactAttribute : FactAttribute
{
    public EndToEndFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("E2E_RUN") != "1")
            Skip = "Echter Anwendungstest: mit Storage-Service.Tests/Run-E2E.ps1 gezielt starten.";
    }
}
