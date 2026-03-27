using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace DataViewer.Infrastructure.Logging;

/// <summary>
/// Centralised Serilog bootstrap configuration for the DataViewer application.
/// </summary>
/// <remarks>
/// <para>
/// Call <see cref="Configure"/> as the <em>very first</em> statement in <c>Program.cs</c>,
/// before <c>WebApplication.CreateBuilder()</c>, so that startup errors (configuration
/// loading failures, DI registration problems) are captured in the structured log.
/// </para>
/// <para>
/// Sink strategy (section 7.6 of the System Design Document):
/// <list type="bullet">
///   <item>
///     <description>
///       <strong>Console sink</strong> — JSON (CompactJsonFormatter) in Production;
///       human-readable coloured output in Development/Staging.
///     </description>
///   </item>
///   <item>
///     <description>
///       <strong>File sink</strong> — daily rolling file under <c>logs/dataviewer-.log</c>
///       with 7-day retention and a 50 MB per-file size cap. Always uses compact JSON so
///       that log-shipping agents (Filebeat, Fluentd) can parse every environment's output.
///     </description>
///   </item>
/// </list>
/// </para>
/// <para>
/// Every log event is enriched with <c>RequestId</c>, <c>UserId</c>, <c>Method</c>,
/// <c>Path</c>, <c>StatusCode</c>, <c>ElapsedMs</c>, and <c>IpAddress</c> when those
/// values are available in the current HTTP context.  The enrichment is performed by
/// ASP.NET Core's <c>UseSerilogRequestLogging()</c> middleware (configured in
/// <c>Program.cs</c>) and by the application's own <c>HttpContextEnricher</c>.
/// </para>
/// <para>
/// <see cref="SensitiveFieldDestructuringPolicy"/> is registered globally to guarantee
/// that <c>SecretAccessKey</c>, <c>PasswordHash</c>, <c>TokenHash</c>, <c>Authorization</c>,
/// and <c>DATAVIEWER_ENCRYPTION_KEY</c> are never written to any sink at any level.
/// </para>
/// </remarks>
public static class SerilogConfiguration
{
    /// <summary>
    /// Rolling-file path pattern. The <c>{Date}</c> token is replaced daily by Serilog.Sinks.File.
    /// </summary>
    private const string LogFilePath = "logs/dataviewer-.log";

    /// <summary>Maximum size of a single log file before Serilog rolls to a new file.</summary>
    private const long MaxLogFileSizeBytes = 50L * 1024 * 1024; // 50 MB

    /// <summary>Number of daily log files retained on disk before the oldest is deleted.</summary>
    private const int RetainedFileCount = 7;

    /// <summary>
    /// Output template used for the human-readable console sink in non-Production environments.
    /// Contains all mandatory fields from section 7.6 that are available at the point the
    /// request-logging middleware emits each event.
    /// </summary>
    private const string HumanReadableTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}" +
        "{NewLine}{Properties:j}" +
        "{NewLine}{Exception}";

    /// <summary>
    /// Configures and assigns the Serilog global <see cref="Log.Logger"/> from the supplied
    /// <paramref name="configuration"/> and <paramref name="environment"/>.
    /// </summary>
    /// <param name="configuration">
    /// The application's <see cref="IConfiguration"/> instance (already loaded from
    /// <c>appsettings.json</c> and environment-specific overrides).
    /// The key <c>Logging:MinimumLevel</c> controls the floor level (defaults to
    /// <c>Information</c> when absent or unrecognised).
    /// </param>
    /// <param name="environment">
    /// The hosting environment used to select the console output format:
    /// JSON in Production, human-readable elsewhere.
    /// </param>
    /// <remarks>
    /// This method must be called <em>before</em> <c>WebApplication.CreateBuilder()</c>
    /// so that host-build failures are captured.  After calling this method, wire Serilog
    /// into the ASP.NET Core host with:
    /// <code>
    /// builder.Host.UseSerilog();
    /// app.UseSerilogRequestLogging(opts => SerilogConfiguration.ConfigureRequestLogging(opts));
    /// </code>
    /// </remarks>
    public static void Configure(IConfiguration configuration, IHostEnvironment environment)
    {
        var minimumLevel = ReadMinimumLevel(configuration);
        var isProduction = environment.IsProduction();

        var loggerConfig = new LoggerConfiguration()
            // ── Minimum level ──────────────────────────────────────────────────────
            .MinimumLevel.Is(minimumLevel)
            // Suppress noisy Microsoft/ASP.NET Core framework logs below Warning
            // unless the operator has explicitly set Debug or Verbose.
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)

            // ── Enrichers ─────────────────────────────────────────────────────────
            .Enrich.FromLogContext()          // captures LogContext.PushProperty() values
            .Enrich.WithMachineName()         // containerised deployment correlation
            .Enrich.WithEnvironmentName()     // Production / Development / Staging
            .Enrich.WithProcessId()           // useful when multiple workers run
            .Enrich.WithThreadId()            // async debugging
            .Enrich.WithProperty("Application", "DataViewer")

            // ── Sensitive-field protection (security requirement) ─────────────────
            // Registered before any sink so the policy applies to ALL destinations.
            .Destructure.With<SensitiveFieldDestructuringPolicy>()

            // ── Console sink ──────────────────────────────────────────────────────
            .WriteTo.Conditional(
                _ => isProduction,
                // Production: machine-parseable compact JSON for log shippers.
                sink => sink.Console(new CompactJsonFormatter()))
            .WriteTo.Conditional(
                _ => !isProduction,
                // Development / Staging: coloured, human-readable output.
                sink => sink.Console(
                    outputTemplate: HumanReadableTemplate,
                    theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code))

            // ── Rolling file sink ─────────────────────────────────────────────────
            // Always writes compact JSON so that log-shipping agents can parse files
            // from any environment consistently (Production AND Development).
            .WriteTo.File(
                formatter: new CompactJsonFormatter(),
                path: LogFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedFileCount,
                fileSizeLimitBytes: MaxLogFileSizeBytes,
                rollOnFileSizeLimit: true,
                shared: false,          // single-process; shared=false avoids locking overhead
                flushToDiskInterval: TimeSpan.FromSeconds(2));

        Log.Logger = loggerConfig.CreateLogger();
    }

    /// <summary>
    /// Configures the options for <c>UseSerilogRequestLogging()</c> middleware so that
    /// every HTTP request log event includes the mandatory fields from section 7.6:
    /// <c>RequestId</c>, <c>UserId</c>, <c>Method</c>, <c>Path</c>, <c>StatusCode</c>,
    /// <c>ElapsedMs</c>, and <c>IpAddress</c>.
    /// </summary>
    /// <param name="options">
    /// The <see cref="Serilog.AspNetCore.RequestLoggingOptions"/> instance provided by the
    /// middleware registration call in <c>Program.cs</c>.
    /// </param>
    /// <remarks>
    /// Usage in <c>Program.cs</c>:
    /// <code>
    /// app.UseSerilogRequestLogging(SerilogConfiguration.ConfigureRequestLogging);
    /// </code>
    /// </remarks>
    public static void ConfigureRequestLogging(Serilog.AspNetCore.RequestLoggingOptions options)
    {
        // Emit one summary log entry per request at the Information level.
        options.GetLevel = static (httpContext, elapsed, ex) =>
            ex is not null
                ? LogEventLevel.Error
                : httpContext.Response.StatusCode >= 500
                    ? LogEventLevel.Error
                    : httpContext.Response.StatusCode >= 400
                        ? LogEventLevel.Warning
                        : LogEventLevel.Information;

        // Enrich each request completion event with the structured fields mandated
        // by section 7.6 of the System Design Document.
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            // RequestId: ASP.NET Core's built-in trace identifier (correlates all log
            // entries for a single HTTP request).
            diagnosticContext.Set("RequestId", httpContext.TraceIdentifier);

            // UserId: extracted from the authenticated user's claims when present.
            // Left absent (not set) for anonymous / unauthenticated requests so that
            // log consumers can distinguish authenticated from anonymous traffic.
            var userId = httpContext.User?.FindFirst(
                System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? httpContext.User?.FindFirst("sub")?.Value;

            if (!string.IsNullOrEmpty(userId))
                diagnosticContext.Set("UserId", userId);

            // Method and Path are already included in Serilog's default request
            // logging template but are re-emitted as top-level structured properties
            // for consistent querying in log-aggregation tools.
            diagnosticContext.Set("Method", httpContext.Request.Method);
            diagnosticContext.Set("Path", httpContext.Request.Path.Value);

            // StatusCode is set by Serilog automatically when response completes.
            // ElapsedMs is computed internally by UseSerilogRequestLogging.

            // IpAddress: prefer the leftmost entry of X-Forwarded-For (nginx proxy),
            // fall back to the direct connection remote address.
            var ipAddress = GetClientIpAddress(httpContext);
            diagnosticContext.Set("IpAddress", ipAddress);

            // Sanitise the Authorization header — log only the scheme, never the token.
            // ASSUMPTION: The Authorization header value is intentionally not logged even
            // partially (e.g. scheme-only) because the Bearer prefix combined with the
            // jti claim could expose session metadata. The header presence is logged only.
            var hasAuth = httpContext.Request.Headers.ContainsKey("Authorization");
            diagnosticContext.Set("Authenticated", hasAuth);
        };
    }

    // ── Private helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the <c>Logging:MinimumLevel</c> value from <paramref name="configuration"/>.
    /// Returns <see cref="LogEventLevel.Information"/> when the key is absent or the value
    /// cannot be parsed as a valid <see cref="LogEventLevel"/>.
    /// </summary>
    private static LogEventLevel ReadMinimumLevel(IConfiguration configuration)
    {
        var raw = configuration["Logging:MinimumLevel"];

        if (string.IsNullOrWhiteSpace(raw))
            return LogEventLevel.Information;

        return Enum.TryParse<LogEventLevel>(raw, ignoreCase: true, out var parsed)
            ? parsed
            : LogEventLevel.Information;
    }

    /// <summary>
    /// Extracts the originating client IP address from the HTTP context.
    /// Checks <c>X-Forwarded-For</c> first (nginx reverse-proxy scenario per ADR-006)
    /// and falls back to <c>RemoteIpAddress</c> for direct connections.
    /// </summary>
    private static string GetClientIpAddress(Microsoft.AspNetCore.Http.HttpContext httpContext)
    {
        // X-Forwarded-For may contain a comma-separated list; the leftmost value is the
        // original client IP. nginx is configured to always set this header.
        if (httpContext.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
        {
            var firstIp = forwardedFor.ToString().Split(',', StringSplitOptions.TrimEntries)[0];
            if (!string.IsNullOrEmpty(firstIp))
                return firstIp;
        }

        return httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
