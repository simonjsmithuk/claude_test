namespace DataViewer.Application.Services;

using DataViewer.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Stateless body-truncation service.
/// Reads up to <c>BodySizeCapMb</c> megabytes from a stream and reports whether
/// the stream contained more data beyond the cap.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Size-cap source:</strong>
/// The cap is read from <see cref="DataViewer.Domain.Entities.SystemSettings.BodySizeCapMb"/>
/// via <see cref="ISystemSettingsRepository"/> on every call, so that Admin changes to
/// the cap are reflected immediately without requiring an application restart.
/// When the settings row cannot be retrieved (e.g. on an unseeded database) the service
/// falls back to <see cref="DefaultBodySizeCapMb"/> (5 MB).
/// </para>
/// <para>
/// <strong>Singleton + Scoped dependency resolution:</strong>
/// <see cref="ISystemSettingsRepository"/> is a Scoped service (it depends on a Scoped
/// <see cref="Microsoft.EntityFrameworkCore.DbContext"/>). Because
/// <see cref="BodyTruncator"/> is registered as a Singleton, it cannot directly hold a
/// constructor-injected <see cref="ISystemSettingsRepository"/> reference — doing so
/// would capture a short-lived service inside a long-lived container (the "captive
/// dependency" anti-pattern). Instead, this class injects <see cref="IServiceScopeFactory"/>
/// and creates a short-lived DI scope per <see cref="TruncateIfNeededAsync"/> call to
/// safely resolve a fresh <see cref="ISystemSettingsRepository"/> instance each time.
/// </para>
/// <para>
/// <strong>Truncation detection:</strong>
/// The service reads up to <c>cap + 1</c> bytes from the stream. If the extra probe
/// byte is successfully read, the stream had more data than the cap and
/// <c>IsTruncated = true</c> is returned along with only the capped bytes.
/// This avoids having to seek the stream (which may not be seekable).
/// </para>
/// <para>
/// <strong>Registration:</strong>
/// Register as Singleton — the class holds no mutable state beyond injected
/// dependencies (<see cref="IServiceScopeFactory"/> and <see cref="ILogger{T}"/>),
/// both of which are designed for concurrent use.
/// </para>
/// </remarks>
public sealed class BodyTruncator : IBodyTruncator
{
    /// <summary>
    /// Fallback body size cap in megabytes used when <see cref="ISystemSettingsRepository"/>
    /// cannot provide a value. The acceptance criteria specify 5 MB.
    /// </summary>
    internal const int DefaultBodySizeCapMb = 5;

    // Read-buffer size for streaming reads from the body stream.
    // 4096 bytes balances memory use against the number of async round-trips.
    private const int ReadBufferSize = 4_096;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BodyTruncator> _logger;

    /// <summary>
    /// Initialises the truncator with its required dependencies.
    /// </summary>
    /// <param name="scopeFactory">
    /// Factory used to create a short-lived DI scope per call so that the Scoped
    /// <see cref="ISystemSettingsRepository"/> can be safely resolved from this
    /// Singleton service without capturing a stale DbContext.
    /// </param>
    /// <param name="logger">Structured logger for diagnostic output.</param>
    public BodyTruncator(
        IServiceScopeFactory scopeFactory,
        ILogger<BodyTruncator> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger       = logger       ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <remarks>
    /// The method reads chunks of up to <see cref="ReadBufferSize"/> bytes in a loop
    /// until either the cap is reached or the stream is exhausted. It then attempts
    /// one additional single-byte read to distinguish "exactly at cap" from "more data
    /// available beyond cap" without seeking.
    /// </remarks>
    public async Task<(byte[] Bytes, bool IsTruncated)> TruncateIfNeededAsync(
        Stream bodyStream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bodyStream);

        var capMb    = await ResolveSizeCapMbAsync(cancellationToken).ConfigureAwait(false);
        var capBytes = capMb * 1024 * 1024; // MB → bytes

        _logger.LogDebug(
            "BodyTruncator: reading stream with cap={CapMb} MB ({CapBytes} bytes).",
            capMb,
            capBytes);

        // Pre-allocate up to 1 MB to avoid excessive growth on small bodies;
        // MemoryStream will grow automatically if the body fills the initial capacity.
        using var buffer = new MemoryStream(capacity: Math.Min(capBytes, 1024 * 1024));
        var readBuffer   = new byte[ReadBufferSize];
        int totalRead    = 0;

        // ── Phase 1: read up to capBytes ──────────────────────────────────────
        while (totalRead < capBytes)
        {
            // Never read beyond the cap boundary in this phase.
            int toRead = Math.Min(ReadBufferSize, capBytes - totalRead);

            int read = await bodyStream
                .ReadAsync(readBuffer.AsMemory(0, toRead), cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
            {
                // Stream exhausted within the cap — not truncated.
                _logger.LogDebug(
                    "BodyTruncator: stream exhausted at {TotalRead} bytes — not truncated.",
                    totalRead);

                return (buffer.ToArray(), false);
            }

            await buffer
                .WriteAsync(readBuffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);

            totalRead += read;
        }

        // ── Phase 2: probe for one additional byte to detect truncation ───────
        // A successful probe read means the stream had more than capBytes available.
        var probeByte = new byte[1];
        int probeRead = await bodyStream
            .ReadAsync(probeByte.AsMemory(), cancellationToken)
            .ConfigureAwait(false);

        bool isTruncated = probeRead > 0;

        if (isTruncated)
        {
            _logger.LogDebug(
                "BodyTruncator: stream exceeded cap of {CapMb} MB — body will be truncated.",
                capMb);
        }
        else
        {
            _logger.LogDebug(
                "BodyTruncator: stream fit exactly within cap of {CapMb} MB — not truncated.",
                capMb);
        }

        // Return only the capped bytes, regardless of whether the stream has more.
        return (buffer.ToArray(), isTruncated);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Resolves <c>BodySizeCapMb</c> from <see cref="ISystemSettingsRepository"/>
    /// inside a short-lived DI scope, safe to call from a Singleton service.
    /// Returns <see cref="DefaultBodySizeCapMb"/> when the repository returns
    /// <see langword="null"/> (unseeded database) or when the stored value is
    /// invalid (less than 1).
    /// </summary>
    private async Task<int> ResolveSizeCapMbAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Create a short-lived scope so we can safely resolve the Scoped
            // ISystemSettingsRepository from this Singleton service.
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider
                .GetRequiredService<ISystemSettingsRepository>();

            var settings = await repository
                .GetAsync(cancellationToken)
                .ConfigureAwait(false);

            if (settings is null)
            {
                _logger.LogWarning(
                    "BodyTruncator: SystemSettings row not found — "
                    + "falling back to default cap of {DefaultCap} MB.",
                    DefaultBodySizeCapMb);
                return DefaultBodySizeCapMb;
            }

            if (settings.BodySizeCapMb < 1)
            {
                _logger.LogWarning(
                    "BodyTruncator: SystemSettings.BodySizeCapMb={Configured} is invalid "
                    + "(must be ≥ 1) — falling back to default cap of {DefaultCap} MB.",
                    settings.BodySizeCapMb,
                    DefaultBodySizeCapMb);
                return DefaultBodySizeCapMb;
            }

            return settings.BodySizeCapMb;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Defensive: if the repository or scope creation throws (e.g. transient DB
            // error), fall back to the default rather than failing the entire body read.
            _logger.LogError(
                ex,
                "BodyTruncator: failed to retrieve SystemSettings — "
                + "falling back to default cap of {DefaultCap} MB.",
                DefaultBodySizeCapMb);
            return DefaultBodySizeCapMb;
        }
    }
}
