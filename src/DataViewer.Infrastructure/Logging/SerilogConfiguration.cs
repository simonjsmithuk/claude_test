// =============================================================================
// SerilogConfiguration — DataViewer.Infrastructure
// =============================================================================
// License notes:
//   Serilog                      Apache 2.0  https://github.com/serilog/serilog
//   Serilog.AspNetCore           Apache 2.0  https://github.com/serilog/serilog-aspnetcore
//   Serilog.Sinks.Console        Apache 2.0  https://github.com/serilog/serilog-sinks-console
//   Serilog.Sinks.File           Apache 2.0  https://github.com/serilog/serilog-sinks-file
//   Serilog.Formatting.Compact   Apache 2.0  https://github.com/serilog/serilog-formatting-compact
//   Serilog.Enrichers.Environment Apache 2.0 https://github.com/serilog/serilog-enrichers-environment
//   Serilog.Enrichers.Thread     Apache 2.0  https://github.com/serilog/serilog-enrichers-thread
//   Serilog.Enrichers.Process    Apache 2.0  https://github.com/serilog/serilog-enrichers-process
// =============================================================================

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
/// Call <see cref="Configure"/> as the <em>very first</em> statement in
/// <c>Program.cs</c>, before <c>WebApplication.CreateBuilder()</c>, so that
/// startup errors (configuration loading failures, DI registration problems) are
/// captured in the structured log:
/// <code>
/// SerilogConfiguration.Configure(bootstrapConfig, bootstrapEnvironment);
/// try
/// {
///     var builder = WebApplication.CreateBuilder(args);
///     builder.Host.UseSerilog();
///     …
///     app.UseSerilogRequestLogging(SerilogConfiguration.ConfigureRequestLogging);
///     await app.RunAsync();
/// }
/// finally { await Log.CloseAndFlushAsync(); }
/// </code>
/// </para>
///
/// <para>
/// <strong>Sink strategy (SDD § 7.6):</strong>
/// <list type="bullet">
///   <item>
///     <description>
///       <b>Console sink</b> — <see cref="CompactJsonFormatter"/> (machine-parseable)
///       in Production; human-readable coloured output via
///       <see cref="Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code"/>
///       in Development/Staging.  Both variants are wrapped by
///       <see cref="SensitivePropertyRedactionSink"/> before any bytes are written.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>File sink</b> — daily rolling file under <c>logs/dataviewer-.log</c>
///       with 7-day retention and a 50 MB per-file size cap.  Always uses
///       <see cref="CompactJsonFormatter"/> so that log-shipping agents (Filebeat,
///       Fluentd) can parse output from every environment consistently.
///       Also wrapped by <see cref="SensitivePropertyRedactionSink"/>.
///     </description>
///   </item>
/// </list>
/// </para>
///
/// <para>
/// <strong>Sensitive-field protection (TASK-020 Acceptance Criteria):</strong>
/// Two complementary mechanisms guarantee that the five protected fields
/// (<c>SecretAccessKey</c>, <c>PasswordHash</c>, <c>TokenHash</c>,
/// <c>Authorization</c>, <c>DATAVIEWER_ENCRYPTION_KEY</c>) never appear in any
/// log output at any level:
/// <list type="number">
///   <item>
///     <description>
///       <see cref="SensitiveFieldDestructuringPolicy"/> — intercepts structured
///       objects destructured with <c>@</c> or <c>destructureObjects: true</c>
///       and replaces matching property values during the destructuring phase.
///     </description>
///   </item>
///   <item>
///     <description>
///       <see cref="SensitivePropertyRedactionSink"/> — wraps every write sink and
///       performs a final pass over each completed <see cref="LogEvent"/>'s property
///       dictionary, replacing any top-level property whose name is in the sensitive
///       set with <c>***REDACTED***</c>.  This catches scalar properties logged
///       directly (e.g. <c>Log.Information("{Authorization}", …)</c> or via
///       <c>LogContext.PushProperty</c>).
///     </description>
///   </item>
/// </list>
/// </para>
///
/// <para>
/// <strong>Required enriched fields per request (SDD § 7.6):</strong>
/// <c>RequestId</c>, <c>UserId</c> (when available), <c>Method</c>, <c>Path</c>,
/// <c>StatusCode</c>, <c>ElapsedMs</c>, and <c>IpAddress</c>.  These are populated
/// by <see cref="ConfigureRequestLogging"/> and the ASP.NET Core Serilog middleware.
/// </para>
/// </remarks>
public static class SerilogConfiguration
{
    // ── File sink constants ───────────────────────────────────────────────────

    /// <summary>
    /// Rolling-file path pattern. Serilog appends the date before the extension
    /// (e.g. <c>logs/dataviewer-20250710.log</c>) when <c>rollingInterval</c>
    /// is set to <see cref="RollingInterval.Day"/>.
    /// </summary>
    private const string LogFilePath = "logs/dataviewer-.log";

    /// <summary>Maximum size of a single log file before Serilog rolls to a new file.</summary>
    private const long MaxLogFileSizeBytes = 50L * 1024 * 1024; // 50 MB

    /// <summary>
    /// Number of daily log files retained on disk before the oldest is deleted.
    /// Acceptance criteria: 7-day retention.
    /// </summary>
    private const int RetainedFileCount = 7;

    // ── Console sink output template ──────────────────────────────────────────

    /// <summary>
    /// Output template for the human-readable console sink used in non-Production
    /// environments.  Includes all mandatory SDD § 7.6 fields that are available
    /// at the point the request-logging middleware emits each event.
    /// </summary>
    /// <remarks>
    /// The <c>{Properties:j}</c> token serialises the entire enriched property bag
    /// as JSON on a second line, ensuring that <c>RequestId</c>, <c>UserId</c>,
    /// <c>Method</c>, <c>Path</c>, <c>StatusCode</c>, <c>ElapsedMs</c>,
    /// <c>IpAddress</c>, <c>MachineName</c>, <c>EnvironmentName</c>, and any other
    /// enriched values are always visible in development output.
    /// </remarks>
    private const string HumanReadableOutputTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}" +
        "{NewLine}{Properties:j}" +
        "{NewLine}{Exception}";

    // ── Framework namespaces to suppress ─────────────────────────────────────

    /// <summary>
    /// Minimum level override applied to the noisy <c>Microsoft.*</c> namespace
    /// tree so that framework-internal log entries below Warning are suppressed
    /// unless the operator has set <c>Logging:MinimumLevel</c> to Debug or Verbose.
    /// </summary>
    private const LogEventLevel FrameworkMinimumLevel = LogEventLevel.Warning;

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Configures and assigns the Serilog global <see cref="Log.Logger"/> from the
    /// supplied <paramref name="configuration"/> and <paramref name="environment"/>.
    /// </summary>
    /// <param name="configuration">
    /// The application's <see cref="IConfiguration"/> instance, already loaded from
    /// <c>appsettings.json</c> and environment-specific overrides.  The key
    /// <c>Logging:MinimumLevel</c> controls the floor level; it defaults to
    /// <see cref="LogEventLevel.Information"/> when absent or unrecognised.
    /// </param>
    /// <param name="environment">
    /// The hosting environment used to select the console output format:
    /// compact JSON in Production, human-readable coloured output elsewhere.
    /// </param>
    /// <remarks>
    /// <para>
    /// This method MUST be called <em>before</em> <c>WebApplication.CreateBuilder()</c>
    /// so that host-build failures are captured by the configured sinks.
    /// </para>
    /// <para>
    /// After calling this method, wire Serilog into the ASP.NET Core host:
    /// <code>
    /// builder.Host.UseSerilog();
    /// app.UseSerilogRequestLogging(SerilogConfiguration.ConfigureRequestLogging);
    /// </code>
    /// </para>
    /// </remarks>
    public static void Configure(IConfiguration configuration, IHostEnvironment environment)
    {
        var minimumLevel = ReadMinimumLevel(configuration);
        var isProduction = environment.IsProduction();

        var loggerConfig = new LoggerConfiguration()

            // ── Minimum level ─────────────────────────────────────────────────
            .MinimumLevel.Is(minimumLevel)
            // Suppress noisy Microsoft/ASP.NET Core framework logs below Warning
            // unless the operator explicitly sets Debug or Verbose.
            .MinimumLevel.Override("Microsoft", FrameworkMinimumLevel)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", FrameworkMinimumLevel)
            .MinimumLevel.Override("System.Net.Http.HttpClient", FrameworkMinimumLevel)

            // ── Enrichers ─────────────────────────────────────────────────────
            // FromLogContext captures any properties pushed via LogContext.PushProperty
            // (e.g. scoped RequestId, UserId injected by middleware).
            .Enrich.FromLogContext()
            // MachineName and EnvironmentName correlate events in multi-host deployments.
            .Enrich.WithMachineName()
            .Enrich.WithEnvironmentName()
            // ProcessId is useful when multiple workers share a log aggregator.
            .Enrich.WithProcessId()
            // ThreadId aids debugging of async operations in structured log queries.
            .Enrich.WithThreadId()
            // Static application-name tag for log aggregators (e.g. Kibana filter).
            .Enrich.WithProperty("Application", "DataViewer")

            // ── Sensitive-field protection: destructuring phase ───────────────
            // Registered before sinks so the policy applies to ALL structured objects
            // destructured anywhere in the pipeline. Handles POCO / record types that
            // are logged with the @ operator or destructureObjects: true.
            .Destructure.With<SensitiveFieldDestructuringPolicy>()

            // ── Write sinks (each wrapped by SensitivePropertyRedactionSink) ──
            // The redaction sink wraps every downstream write sink and performs a
            // final pass over each completed LogEvent's top-level properties,
            // catching scalar-valued sensitive properties that bypass destructuring
            // (e.g. Log.Information("{Authorization}", value) or PushProperty(...)).
            .WriteTo.Sink(BuildConsoleSink(isProduction))
            .WriteTo.Sink(BuildFileSink());

        Log.Logger = loggerConfig.CreateLogger();
    }

    /// <summary>
    /// Configures the options for <c>UseSerilogRequestLogging()</c> middleware so that
    /// every HTTP request log event includes the mandatory fields from SDD § 7.6:
    /// <c>RequestId</c>, <c>UserId</c>, <c>Method</c>, <c>Path</c>, <c>StatusCode</c>,
    /// <c>ElapsedMs</c>, and <c>IpAddress</c>.
    /// </summary>
    /// <param name="options">
    /// The <see cref="Serilog.AspNetCore.RequestLoggingOptions"/> instance provided by
    /// the middleware registration call in <c>Program.cs</c>.
    /// </param>
    /// <remarks>
    /// Usage in <c>Program.cs</c>:
    /// <code>
    /// app.UseSerilogRequestLogging(SerilogConfiguration.ConfigureRequestLogging);
    /// </code>
    /// </remarks>
    public static void ConfigureRequestLogging(Serilog.AspNetCore.RequestLoggingOptions options)
    {
        // Emit one summary log entry per request.  Level is promoted to Error for
        // 5xx responses and unhandled exceptions, Warning for 4xx, Information otherwise.
        options.GetLevel = static (httpContext, elapsed, ex) =>
            ex is not null
                ? LogEventLevel.Error
                : httpContext.Response.StatusCode >= 500
                    ? LogEventLevel.Error
                    : httpContext.Response.StatusCode >= 400
                        ? LogEventLevel.Warning
                        : LogEventLevel.Information;

        // Enrich each request completion event with the structured properties
        // mandated by SDD § 7.6.
        options.EnrichDiagnosticContext = static (diagnosticContext, httpContext) =>
        {
            // ── RequestId ──────────────────────────────────────────────────────
            // ASP.NET Core's built-in trace identifier; correlates every log entry
            // emitted during the lifetime of a single HTTP request.
            diagnosticContext.Set("RequestId", httpContext.TraceIdentifier);

            // ── UserId ─────────────────────────────────────────────────────────
            // Extracted from the authenticated user's claims. Left absent for
            // anonymous requests so log consumers can distinguish authenticated
            // from anonymous traffic without a null/empty noise value.
            var userId = httpContext.User?.FindFirst(
                System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? httpContext.User?.FindFirst("sub")?.Value;

            if (!string.IsNullOrEmpty(userId))
                diagnosticContext.Set("UserId", userId);

            // ── Method & Path ──────────────────────────────────────────────────
            // Serilog's default request-logging template includes {RequestMethod}
            // and {RequestPath}, but explicit structured properties are set here
            // for consistent field names in log-aggregation queries (SDD § 7.6).
            diagnosticContext.Set("Method", httpContext.Request.Method);
            diagnosticContext.Set("Path", httpContext.Request.Path.Value);

            // ── StatusCode — emitted automatically by Serilog middleware ───────
            // ── ElapsedMs  — computed internally by UseSerilogRequestLogging ───
            // Both are already present in every request-log event as {StatusCode}
            // and {Elapsed} respectively; no explicit set is required here.

            // ── IpAddress ──────────────────────────────────────────────────────
            // Prefer the leftmost entry of X-Forwarded-For (the nginx reverse-proxy
            // path per ADR-006). Fall back to the direct connection remote address.
            var ipAddress = GetClientIpAddress(httpContext);
            diagnosticContext.Set("IpAddress", ipAddress);

            // ── Authorization header ───────────────────────────────────────────
            // Log only the presence/absence of the header, never its value.
            // The bearer token is protected by SensitivePropertyRedactionSink
            // (which would redact it if logged as {Authorization}), but belt-and-
            // suspenders: we deliberately choose NOT to set the "Authorization"
            // property at all here and instead emit only a boolean.
            // ASSUMPTION: Logging scheme-only (e.g. "Bearer") is considered
            // unnecessary metadata overhead; a boolean "Authenticated" flag is
            // sufficient for audit/diagnostic purposes.
            var hasAuth = httpContext.Request.Headers.ContainsKey("Authorization");
            diagnosticContext.Set("Authenticated", hasAuth);
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers — sink construction
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the console sink, wrapping it with
    /// <see cref="SensitivePropertyRedactionSink"/> for final-pass redaction.
    /// </summary>
    /// <param name="isProduction">
    /// <see langword="true"/> → <see cref="CompactJsonFormatter"/> (machine-parseable);
    /// <see langword="false"/> → themed human-readable output for Development/Staging.
    /// </param>
    private static SensitivePropertyRedactionSink BuildConsoleSink(bool isProduction)
    {
        // Use a sub-logger confined to the console sink so that we can wrap it
        // with the redaction decorator without affecting the file sink.
        var consoleSinkConfig = new LoggerConfiguration()
            .MinimumLevel.Verbose() // floor set by parent; sub-logger passes everything
            .WriteTo.Conditional(
                _ => isProduction,
                // Production: machine-parseable compact JSON for log-shipping agents.
                sink => sink.Console(new CompactJsonFormatter()))
            .WriteTo.Conditional(
                _ => !isProduction,
                // Development/Staging: coloured, human-readable output.
                sink => sink.Console(
                    outputTemplate: HumanReadableOutputTemplate,
                    theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code))
            .CreateLogger();

        // Wrap the console sub-logger sink in the redaction decorator.
        // LoggerSinkConfiguration.Logger() returns an ILogEventSink backed by
        // the sub-logger; we compose it with SensitivePropertyRedactionSink.
        return new SensitivePropertyRedactionSink(consoleSinkConfig);
    }

    /// <summary>
    /// Builds the rolling-file sink, wrapping it with
    /// <see cref="SensitivePropertyRedactionSink"/> for final-pass redaction.
    /// </summary>
    /// <remarks>
    /// Always uses <see cref="CompactJsonFormatter"/> regardless of environment so
    /// that log-shipping agents (Filebeat, Fluentd) can parse files from every
    /// environment consistently.
    /// </remarks>
    private static SensitivePropertyRedactionSink BuildFileSink()
    {
        var fileSinkConfig = new LoggerConfiguration()
            .MinimumLevel.Verbose() // floor set by parent; sub-logger passes everything
            .WriteTo.File(
                formatter: new CompactJsonFormatter(),
                path: LogFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedFileCount,
                fileSizeLimitBytes: MaxLogFileSizeBytes,
                // Roll to a new file when the size limit is reached mid-day so that
                // log files never grow unboundedly if log volume spikes unexpectedly.
                rollOnFileSizeLimit: true,
                // Single-process; shared: false avoids file-locking overhead.
                // ASSUMPTION: Horizontal scaling (multiple processes per host) is not
                // anticipated in the initial Ubuntu deployment; adjust to shared: true
                // or switch to a network sink if multi-process writes are required.
                shared: false,
                // Flush to disk every 2 seconds to balance write latency against
                // the risk of losing the last few events on abnormal process exit.
                flushToDiskInterval: TimeSpan.FromSeconds(2))
            .CreateLogger();

        return new SensitivePropertyRedactionSink(fileSinkConfig);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers — configuration reading
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the <c>Logging:MinimumLevel</c> value from <paramref name="configuration"/>.
    /// Returns <see cref="LogEventLevel.Information"/> when the key is absent or the
    /// value cannot be parsed as a valid <see cref="LogEventLevel"/>.
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

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers — HTTP context
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the originating client IP address from the HTTP context.
    /// Checks the <c>X-Forwarded-For</c> header first (nginx reverse-proxy scenario
    /// per ADR-006) and falls back to <see cref="Microsoft.AspNetCore.Http.ConnectionInfo.RemoteIpAddress"/>
    /// for direct connections.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <returns>
    /// The client IP address string, or <c>"unknown"</c> when neither source is available.
    /// </returns>
    private static string GetClientIpAddress(Microsoft.AspNetCore.Http.HttpContext httpContext)
    {
        // X-Forwarded-For may contain a comma-separated list; the leftmost value is
        // the original client IP.  nginx is configured (ADR-006) to always set this header
        // before forwarding to Kestrel.
        if (httpContext.Request.Headers.TryGetValue(
                "X-Forwarded-For", out var forwardedFor))
        {
            var firstIp = forwardedFor.ToString()
                .Split(',', StringSplitOptions.TrimEntries)[0];

            if (!string.IsNullOrEmpty(firstIp))
                return firstIp;
        }

        return httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
