namespace DataViewer.Application.DependencyInjection;

using DataViewer.Application.Interfaces;
using DataViewer.Application.Services;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods on <see cref="IServiceCollection"/> that register all
/// Application-layer utility services with the DI container.
/// </summary>
/// <remarks>
/// <para>
/// Call this method from the API project's composition root (<c>Program.cs</c>):
/// <code>
/// builder.Services.AddApplicationServices();
/// </code>
/// </para>
/// <para>
/// <strong>Registration order:</strong>
/// <c>AddApplicationServices()</c> must be called <em>after</em>
/// <c>AddInfrastructure()</c> in the composition root so that
/// <see cref="ISystemSettingsRepository"/> (consumed by
/// <see cref="Services.BodyTruncator"/> via <see cref="IServiceScopeFactory"/>)
/// and <see cref="IAuditRepository"/> (consumed by <see cref="AuditService"/>)
/// are already registered when the Application services are resolved.
/// </para>
/// <para>
/// The Application layer must never reference Infrastructure concrete types.
/// All service registrations bind <em>Application interfaces</em> to
/// <em>Application concrete implementations</em> only.
/// </para>
/// </remarks>
public static class ApplicationServiceExtensions
{
    /// <summary>
    /// Registers all Application-layer utility services into <paramref name="services"/>.
    /// </summary>
    /// <param name="services">The application's service collection.</param>
    /// <returns>
    /// The same <paramref name="services"/> instance to support method chaining.
    /// </returns>
    /// <remarks>
    /// <strong>Service lifetimes:</strong>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       <see cref="IGzipDecompressor"/> → <see cref="GzipDecompressor"/> (<strong>Singleton</strong>).
    ///       Fully stateless. Detects gzip by magic bytes (<c>0x1F 0x8B</c>) or by
    ///       a <c>Content-Encoding: gzip</c> header, then decompresses on-the-fly via
    ///       <see cref="System.IO.Compression.GZipStream"/> without duplicating the
    ///       compressed payload in memory.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="IContentTypeDetector"/> → <see cref="ContentTypeDetector"/> (<strong>Singleton</strong>).
    ///       Fully stateless. Classifies body bytes as
    ///       <see cref="DataViewer.Domain.Enums.BodyContentType.Json"/>,
    ///       <see cref="DataViewer.Domain.Enums.BodyContentType.Xml"/>, or
    ///       <see cref="DataViewer.Domain.Enums.BodyContentType.Text"/> by sniffing
    ///       the first non-whitespace byte(s) of the payload.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="IBodyTruncator"/> → <see cref="BodyTruncator"/> (<strong>Singleton</strong>).
    ///       Reads up to <c>BodySizeCapMb</c> megabytes from a stream, reporting
    ///       <c>IsTruncated = true</c> when the stream holds more data.
    ///       Although <see cref="ISystemSettingsRepository"/> is Scoped, the
    ///       <see cref="BodyTruncator"/> resolves it safely through
    ///       <see cref="IServiceScopeFactory"/> — creating a short-lived scope per
    ///       call rather than capturing a DbContext in the Singleton's constructor.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="IAuditService"/> → <see cref="AuditService"/> (<strong>Scoped</strong>).
    ///       Must be Scoped (not Singleton) because <see cref="AuditService"/> depends
    ///       on <see cref="IAuditRepository"/>, which is Scoped (it holds a reference to
    ///       the request-scoped <see cref="Microsoft.EntityFrameworkCore.DbContext"/> for
    ///       read operations). Registering <see cref="AuditService"/> as Singleton would
    ///       capture a short-lived repository inside a long-lived container — the
    ///       "captive dependency" anti-pattern (Review §3c).
    ///     </description>
    ///   </item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // ── GzipDecompressor — Singleton ──────────────────────────────────────
        // No mutable state. Safe for concurrent use without locking.
        services.AddSingleton<IGzipDecompressor, GzipDecompressor>();

        // ── ContentTypeDetector — Singleton ───────────────────────────────────
        // No mutable state. All detection logic is pure computation over input spans.
        services.AddSingleton<IContentTypeDetector, ContentTypeDetector>();

        // ── BodyTruncator — Singleton ─────────────────────────────────────────
        // Holds IServiceScopeFactory (Singleton) and ILogger<BodyTruncator> (Singleton).
        // Resolves ISystemSettingsRepository (Scoped) on each invocation via a
        // short-lived scope, avoiding the captive dependency anti-pattern.
        services.AddSingleton<IBodyTruncator, BodyTruncator>();

        // ── AuditService — Scoped ─────────────────────────────────────────────
        // Scoped lifetime is required because AuditService depends on IAuditRepository,
        // which is itself Scoped (holds a reference to the request-scoped AppDbContext
        // for read operations via GetPagedAsync). Registering as Singleton here would
        // create a captive dependency: the Singleton AuditService would capture the
        // first-resolved IAuditRepository instance (tied to a single HTTP request's
        // DbContext) and reuse it across all subsequent requests, causing stale data,
        // connection-lifetime violations, and potential data corruption (Review §3c).
        services.AddScoped<IAuditService, AuditService>();

        return services;
    }
}
