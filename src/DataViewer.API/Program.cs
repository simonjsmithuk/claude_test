// =============================================================================
// DataViewer.API — Application entry point
// =============================================================================
// Clean Architecture dependency note:
//   Program.cs is the composition root. It may reference Infrastructure types
//   for DI registration purposes only.  Business logic MUST NOT live here.
// =============================================================================

using DataViewer.Infrastructure.Logging;
using Microsoft.Extensions.Hosting;
using Serilog;

// ── Step 1: Bootstrap early configuration ─────────────────────────────────────
// Load appsettings.json + appsettings.{Environment}.json before anything else so
// that SerilogConfiguration.Configure() can read Logging:MinimumLevel.
// This two-phase approach (bootstrap config → Serilog → full host build) ensures
// that host-build failures and DI registration errors are captured in the log.

var bootstrapConfig = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile(
        $"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json",
        optional: true,
        reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

// Determine hosting environment early so SerilogConfiguration can pick the
// correct console formatter (JSON vs human-readable).
var bootstrapEnvironment = new BootstrapHostEnvironment(
    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environments.Production);

// ── Step 2: Configure Serilog BEFORE building the host ────────────────────────
// Acceptance criterion: "SerilogConfiguration.Configure() is a static method
// called in Program.cs before the host is built." (TASK-020)
SerilogConfiguration.Configure(bootstrapConfig, bootstrapEnvironment);

try
{
    Log.Information("Starting DataViewer API host");

    // ── Step 3: Build the WebApplication host ─────────────────────────────────
    var builder = WebApplication.CreateBuilder(args);

    // Replace the default .NET logging pipeline with Serilog so that all
    // ILogger<T> injections route through the configured sinks and destructuring
    // policies — including SensitiveFieldDestructuringPolicy.
    builder.Host.UseSerilog();

    // ── Service registrations (placeholder — expanded in TASK-025) ────────────
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();

    // TODO TASK-025: Register repositories, use cases, auth, EF Core, CORS,
    //               health checks, rate limiting, Swagger.

    var app = builder.Build();

    // ── Middleware pipeline ────────────────────────────────────────────────────
    // Serilog request logging middleware emits one structured log entry per HTTP
    // request, enriched with RequestId, UserId, Method, Path, StatusCode,
    // ElapsedMs, and IpAddress as required by section 7.6 of the SDD.
    app.UseSerilogRequestLogging(SerilogConfiguration.ConfigureRequestLogging);

    // TODO TASK-026: Add GlobalExceptionMiddleware, SecurityHeadersMiddleware.
    // TODO TASK-025: Add Authentication, Authorization, RateLimit, CORS middleware.

    app.MapControllers();

    await app.RunAsync();

    return 0;
}
catch (Exception ex)
{
    // Capture fatal startup errors before Serilog is torn down.
    Log.Fatal(ex, "DataViewer API host terminated unexpectedly during startup");
    return 1;
}
finally
{
    // Flush all buffered log events and release file handles before the process exits.
    await Log.CloseAndFlushAsync();
}

// ── Internal helper: minimal IHostEnvironment for the bootstrap phase ─────────
/// <summary>
/// Minimal <see cref="IHostEnvironment"/> implementation used only during the
/// Serilog bootstrap phase, before the full ASP.NET Core host is built.
/// </summary>
/// <remarks>
/// This avoids taking a dependency on the full host builder just to determine the
/// environment name for the console sink format selection.
/// </remarks>
internal sealed class BootstrapHostEnvironment : IHostEnvironment
{
    public BootstrapHostEnvironment(string environmentName)
    {
        EnvironmentName = environmentName;
        // ASSUMPTION: ApplicationName and ContentRootPath are not used by
        // SerilogConfiguration.Configure(); safe to leave as defaults here.
        ApplicationName = "DataViewer";
        ContentRootPath = Directory.GetCurrentDirectory();
        ContentRootFileProvider =
            new Microsoft.Extensions.FileProviders.PhysicalFileProvider(ContentRootPath);
    }

    /// <inheritdoc />
    public string EnvironmentName { get; set; }

    /// <inheritdoc />
    public string ApplicationName { get; set; }

    /// <inheritdoc />
    public string ContentRootPath { get; set; }

    /// <inheritdoc />
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
}
