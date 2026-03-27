namespace DataViewer.Application.Services;

using System.Text.Json;
using System.Text.Json.Serialization;
using DataViewer.Application.Interfaces;
using DataViewer.Domain.Entities;
using DataViewer.Domain.Enums;
using DataViewer.Domain.Exceptions;
using DataViewer.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

/// <summary>
/// Application-layer service that writes structured audit log entries for every
/// auditable operation in DataViewer (Product Spec § G-03, ADR-009).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Audit-first contract:</strong>
/// Every public method calls <see cref="IAuditRepository.InsertAsync"/> and
/// <c>await</c>s its completion before returning. If the write fails, an
/// <see cref="AuditFailureException"/> propagates to the caller, which must not
/// return the requested data to the client.
/// </para>
///
/// <para>
/// <strong>Exception handling:</strong>
/// <list type="bullet">
///   <item>
///     <description>
///       <see cref="AuditFailureException"/> — propagated as-is (no double-wrapping).
///     </description>
///   </item>
///   <item>
///     <description>
///       <see cref="OperationCanceledException"/> — propagated as-is (cooperative
///       cancellation must not be disguised as an audit failure).
///     </description>
///   </item>
///   <item>
///     <description>
///       All other exceptions — wrapped in a new <see cref="AuditFailureException"/>
///       so the caller always receives a single typed domain exception.
///     </description>
///   </item>
/// </list>
/// </para>
///
/// <para>
/// <strong>Timestamp discipline:</strong>
/// Each method captures <c>DateTime.UtcNow</c> in a local variable exactly once
/// at the start of the method and passes that value to the
/// <see cref="AuditLogEntry"/> factory. Multiple calls to <c>DateTime.UtcNow</c>
/// within the same method are avoided to prevent clock-tick skew between building
/// the entity and logging it (Performance §P2).
/// </para>
///
/// <para>
/// <strong>JSON serialisation:</strong>
/// The static <see cref="AuditJsonOptions"/> field is initialised once and reused
/// on every call, avoiding the per-call overhead of re-instantiating
/// <see cref="JsonSerializerOptions"/> (Performance §P1).
/// </para>
///
/// <para>
/// <strong>Lifetime:</strong>
/// Must be registered as <em>Scoped</em> in DI. It depends on
/// <see cref="IAuditRepository"/>, which is Scoped; registering as Singleton would
/// create a captive dependency anti-pattern.
/// </para>
/// </remarks>
public sealed class AuditService : IAuditService
{
    // ── Static JSON serialiser options ────────────────────────────────────────
    // Defined as static readonly to avoid re-allocating JsonSerializerOptions on
    // every method call — a documented performance trap with System.Text.Json.
    // CamelCase naming matches the standard API response convention.
    // WriteIndented = false keeps the stored JSON compact in the database column.
    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IAuditRepository _auditRepository;
    private readonly ILogger<AuditService> _logger;

    /// <summary>
    /// Initialises the service with its required dependencies.
    /// </summary>
    /// <param name="auditRepository">
    /// The append-only repository that persists audit entries on an independent
    /// database scope so audit INSERTs are not affected by the calling request's
    /// ambient transaction.
    /// </param>
    /// <param name="logger">Structured logger for diagnostic and error events.</param>
    public AuditService(
        IAuditRepository auditRepository,
        ILogger<AuditService> logger)
    {
        _auditRepository = auditRepository
            ?? throw new ArgumentNullException(nameof(auditRepository));
        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Public interface methods
    // ════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    /// <remarks>
    /// The <paramref name="filter"/> is serialised via a dedicated
    /// <see cref="SearchAuditParameters"/> projection that explicitly excludes
    /// pagination fields (<c>Page</c>, <c>PageSize</c>) which are not meaningful
    /// as long-term audit context. All remaining filter fields are included because
    /// they describe the scope of the data the user queried.
    /// </remarks>
    public async Task LogSearchAsync(
        Guid userId,
        string? ipAddress,
        SearchFilter filter,
        int resultCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        // Capture timestamp once — avoids clock-tick skew between entity creation
        // and any subsequent logging calls within this method (Performance §P2).
        var timestamp = DateTime.UtcNow;

        _logger.LogDebug(
            "Writing {ActionType} audit entry for user {UserId} — resultCount={ResultCount}",
            AuditActionType.SearchTransactions,
            userId,
            resultCount);

        // Serialise filter using a dedicated projection DTO so we never
        // risk accidentally including future sensitive fields added to SearchFilter.
        // Serialisation is done before building the entity (synchronous; §P1).
        var parameters = SerialiseSearchFilter(filter);

        var entry = AuditLogEntry.CreateForUser(
            userId: userId,
            actionType: AuditActionType.SearchTransactions,
            timestampUtc: timestamp,
            ipAddress: ipAddress,
            parameters: parameters,
            resultCount: resultCount);

        await BuildAndInsertAsync(
            entry,
            AuditActionType.SearchTransactions.ToString(),
            cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task LogViewAsync(
        Guid userId,
        string? ipAddress,
        string s3ObjectKey,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(s3ObjectKey);

        var timestamp = DateTime.UtcNow;

        _logger.LogDebug(
            "Writing {ActionType} audit entry for user {UserId} — s3ObjectKey={S3ObjectKey}",
            AuditActionType.ViewTransaction,
            userId,
            s3ObjectKey);

        var entry = AuditLogEntry.CreateForUser(
            userId: userId,
            actionType: AuditActionType.ViewTransaction,
            timestampUtc: timestamp,
            ipAddress: ipAddress,
            s3ObjectKey: s3ObjectKey);

        await BuildAndInsertAsync(
            entry,
            AuditActionType.ViewTransaction.ToString(),
            cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task LogCredentialActionAsync(
        Guid userId,
        string? ipAddress,
        AuditActionType actionType,
        string profileName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);

        var timestamp = DateTime.UtcNow;

        _logger.LogDebug(
            "Writing {ActionType} audit entry for user {UserId} — profileName={ProfileName}",
            actionType,
            userId,
            profileName);

        // Route to CreateForSystem when userId is Guid.Empty to handle
        // any system-initiated credential actions (defensive guard — per S4).
        AuditLogEntry entry = userId == Guid.Empty
            ? AuditLogEntry.CreateForSystem(
                actionType: actionType,
                timestampUtc: timestamp)
            : AuditLogEntry.CreateForUser(
                userId: userId,
                actionType: actionType,
                timestampUtc: timestamp,
                ipAddress: ipAddress,
                profileName: profileName);

        await BuildAndInsertAsync(
            entry,
            actionType.ToString(),
            cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// When <paramref name="userId"/> is <see cref="Guid.Empty"/> (e.g. a background
    /// job triggering an <see cref="AuditActionType.AccountLocked"/> event with no
    /// authenticated principal), the entry is created via
    /// <see cref="AuditLogEntry.CreateForSystem"/> rather than
    /// <see cref="AuditLogEntry.CreateForUser"/>.
    /// <see cref="AuditLogEntry.CreateForUser"/> throws <see cref="ArgumentException"/>
    /// on <see cref="Guid.Empty"/>, so routing to <c>CreateForSystem</c> prevents
    /// an unhandled exception during system-initiated lockout flows (Security §S4).
    /// </remarks>
    public async Task LogAuthAsync(
        Guid userId,
        string? ipAddress,
        AuditActionType actionType,
        CancellationToken cancellationToken)
    {
        var timestamp = DateTime.UtcNow;

        _logger.LogDebug(
            "Writing {ActionType} audit entry for user {UserId}",
            actionType,
            userId);

        // Guid.Empty signals a system-initiated action (e.g. AccountLocked from a
        // background job). CreateForUser would throw ArgumentException on Guid.Empty.
        AuditLogEntry entry = userId == Guid.Empty
            ? AuditLogEntry.CreateForSystem(
                actionType: actionType,
                timestampUtc: timestamp)
            : AuditLogEntry.CreateForUser(
                userId: userId,
                actionType: actionType,
                timestampUtc: timestamp,
                ipAddress: ipAddress);

        await BuildAndInsertAsync(
            entry,
            actionType.ToString(),
            cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task LogAdminActionAsync(
        Guid userId,
        string? ipAddress,
        AuditActionType actionType,
        string details,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(details);

        var timestamp = DateTime.UtcNow;

        _logger.LogDebug(
            "Writing {ActionType} audit entry for user {UserId} — details={Details}",
            actionType,
            userId,
            details);

        var entry = AuditLogEntry.CreateForUser(
            userId: userId,
            actionType: actionType,
            timestampUtc: timestamp,
            ipAddress: ipAddress,
            parameters: details);

        await BuildAndInsertAsync(
            entry,
            actionType.ToString(),
            cancellationToken)
            .ConfigureAwait(false);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Private helpers
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Persists <paramref name="entry"/> via the repository, applying the shared
    /// exception-handling contract for all four public methods:
    /// <list type="bullet">
    ///   <item><description>
    ///     <see cref="AuditFailureException"/> — propagated as-is (no double-wrapping).
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="OperationCanceledException"/> — propagated as-is.
    ///   </description></item>
    ///   <item><description>
    ///     All other exceptions — wrapped as <see cref="AuditFailureException"/>.
    ///   </description></item>
    /// </list>
    /// </summary>
    /// <param name="entry">The fully-populated <see cref="AuditLogEntry"/> to persist.</param>
    /// <param name="auditedAction">
    /// Short identifier of the action (used as
    /// <see cref="AuditFailureException.AuditedAction"/> on the wrapping exception).
    /// </param>
    /// <param name="cancellationToken">Token to observe for cooperative cancellation.</param>
    private async Task BuildAndInsertAsync(
        AuditLogEntry entry,
        string auditedAction,
        CancellationToken cancellationToken)
    {
        try
        {
            await _auditRepository
                .InsertAsync(entry, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogDebug(
                "Audit entry {EntryId} written successfully (ActionType={ActionType})",
                entry.Id,
                entry.ActionType);
        }
        catch (AuditFailureException)
        {
            // AuditRepository already wraps database exceptions as AuditFailureException.
            // Re-throw without wrapping again to avoid double-nesting (Review §3d).
            throw;
        }
        catch (OperationCanceledException)
        {
            // Cooperative cancellation — not an audit subsystem failure.
            // Propagate without wrapping so callers can distinguish cancellation
            // from genuine persistence errors.
            throw;
        }
        catch (Exception ex)
        {
            // Any unexpected exception that was NOT already an AuditFailureException
            // (e.g. serialisation error, argument violation) must be wrapped so
            // the caller always receives a typed domain exception (Acceptance Criteria §8).
            _logger.LogError(
                ex,
                "Unexpected error writing audit entry for action '{AuditedAction}': {Message}",
                auditedAction,
                ex.Message);

            throw new AuditFailureException(
                $"Unexpected error writing audit entry for action '{auditedAction}': {ex.Message}",
                auditedAction: auditedAction,
                innerException: ex);
        }
    }

    /// <summary>
    /// Serialises the auditable subset of <paramref name="filter"/> to a compact
    /// JSON string using <see cref="SearchAuditParameters"/> as an explicit,
    /// auditable exclusion layer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Using a dedicated projection DTO rather than serialising <see cref="SearchFilter"/>
    /// directly creates an explicit, version-controlled list of which fields appear in
    /// the audit log. Any field that should not appear in the log is simply absent from
    /// <see cref="SearchAuditParameters"/>. Future additions to <see cref="SearchFilter"/>
    /// do not automatically leak into audit records unless they are explicitly mirrored
    /// here (Security §S3).
    /// </para>
    /// <para>
    /// The serialiser call is synchronous (<see cref="JsonSerializer.Serialize{T}"/>) —
    /// the value-object is small and JSON serialisation of it is negligible in cost.
    /// Introducing async wrappers would add unnecessary overhead (Performance §P1).
    /// </para>
    /// </remarks>
    private static string SerialiseSearchFilter(SearchFilter filter)
    {
        // ASSUMPTION: Page and PageSize are intentionally excluded from the audit
        // Parameters because they describe navigational context, not the query scope.
        // The meaningful audit data is the filter criteria (what the user was looking
        // for), not which page of results they were viewing.
        var projection = new SearchAuditParameters(filter);

        // JsonSerializer.Serialize never returns null for a non-null input object.
        return JsonSerializer.Serialize(projection, AuditJsonOptions);
    }
}

// ════════════════════════════════════════════════════════════════════════════
// Internal serialisation DTO — SearchAuditParameters
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Dedicated JSON serialisation projection for <see cref="SearchFilter"/> audit
/// parameters. This type defines the <em>exact</em> set of fields that are written
/// to the <c>Parameters</c> column of an audit log entry for a search operation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why a projection DTO rather than serialising <see cref="SearchFilter"/> directly:</strong>
/// <list type="bullet">
///   <item>
///     <description>
///       Creates an explicit, auditable exclusion list. Fields excluded here are
///       guaranteed never to appear in audit records, regardless of future changes
///       to <see cref="SearchFilter"/> (Security §S3, Review §3b).
///     </description>
///   </item>
///   <item>
///     <description>
///       <c>Page</c> and <c>PageSize</c> are intentionally excluded — they describe
///       navigational context (which page the user was on), not the query scope
///       (what the user was searching for). Including them would bloat audit entries
///       with operationally irrelevant data.
///     </description>
///   </item>
///   <item>
///     <description>
///       All serialised values pass through <see cref="JsonSerializer.Serialize"/>
///       — never string interpolation. This prevents log-injection attacks
///       (Security §S1, OWASP A03).
///     </description>
///   </item>
/// </list>
/// </para>
///
/// <para>
/// <strong>Exclusion list (documented for auditability):</strong>
/// <list type="bullet">
///   <item><description><c>Page</c> — navigational, not query scope.</description></item>
///   <item><description><c>PageSize</c> — navigational, not query scope.</description></item>
/// </list>
/// </para>
/// </remarks>
internal sealed class SearchAuditParameters
{
    /// <summary>
    /// Initialises the projection from a <see cref="SearchFilter"/> value object.
    /// </summary>
    /// <param name="filter">The filter whose auditable fields are to be captured.</param>
    internal SearchAuditParameters(SearchFilter filter)
    {
        FromDate    = filter.FromDate;
        ToDate      = filter.ToDate;
        StatusCode  = filter.StatusCode;
        StatusClass = filter.StatusClass;
        Method      = filter.Method;
        UrlPrefix   = filter.UrlPrefix;
        // ASSUMPTION: Page and PageSize are intentionally not mapped here.
        // See class-level XML doc for the rationale.
    }

    /// <summary>
    /// Inclusive lower bound on transaction timestamp. <see langword="null"/> = no lower bound.
    /// </summary>
    public DateTimeOffset? FromDate { get; }

    /// <summary>
    /// Inclusive upper bound on transaction timestamp. <see langword="null"/> = no upper bound.
    /// </summary>
    public DateTimeOffset? ToDate { get; }

    /// <summary>
    /// Exact HTTP status code filter. <see langword="null"/> = any status code.
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>
    /// HTTP status-class prefix filter (e.g. <c>"2"</c> for 2xx).
    /// <see langword="null"/> = any status class.
    /// </summary>
    public string? StatusClass { get; }

    /// <summary>
    /// HTTP method filter (e.g. <c>"GET"</c>). <see langword="null"/> = any method.
    /// </summary>
    public string? Method { get; }

    /// <summary>
    /// URL path prefix filter. <see langword="null"/> = any path.
    /// </summary>
    /// <remarks>
    /// <c>UrlPrefix</c> is included here because it directly describes the scope
    /// of the data the user was searching for, which is core audit information.
    /// It is user-typed text and is therefore serialised through
    /// <see cref="JsonSerializer.Serialize"/> (never string-concatenated) to
    /// prevent log-injection (Security §S1).
    /// </remarks>
    public string? UrlPrefix { get; }
}
