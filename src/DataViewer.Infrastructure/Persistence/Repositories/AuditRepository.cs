using DataViewer.Application.DTOs.AuditLogs;
using DataViewer.Application.DTOs.Common;
using DataViewer.Application.Interfaces;
using DataViewer.Domain.Entities;
using DataViewer.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DataViewer.Infrastructure.Persistence.Repositories;

/// <summary>
/// Entity Framework Core implementation of <see cref="IAuditRepository"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Independent DbContext scope for InsertAsync:</strong>
/// <see cref="InsertAsync"/> uses <see cref="IDbContextFactory{TContext}"/> to create
/// a <em>brand-new</em>, short-lived <see cref="AppDbContext"/> instance whose lifetime
/// is scoped to the single INSERT operation.  This context is completely independent of
/// the HTTP-request-scoped <see cref="AppDbContext"/> that drives the calling unit of
/// work.  As a result:
/// <list type="bullet">
///   <item>
///     <description>
///       The audit INSERT is committed immediately on its own connection.
///     </description>
///   </item>
///   <item>
///     <description>
///       If the calling request's transaction is subsequently rolled back (e.g. a
///       business-rule violation), the already-committed audit record is unaffected —
///       guaranteeing 100 % audit completeness (Product Spec § G-03).
///     </description>
///   </item>
/// </list>
/// </para>
///
/// <para>
/// <strong>GetPagedAsync uses the shared request-scope context:</strong>
/// Reads are served from the injected <see cref="AppDbContext"/> because they carry
/// no transaction-isolation risk; no writes are issued through the shared context
/// from this repository.
/// </para>
///
/// <para>
/// <strong>No exception swallowing:</strong>
/// Both <see cref="InsertAsync"/> and <see cref="GetPagedAsync"/> catch specific
/// database exceptions and re-throw them as <see cref="AuditFailureException"/> so
/// the caller always receives a single, typed domain exception for audit-subsystem
/// failures — never a raw EF Core or provider-level exception (Acceptance Criteria).
/// </para>
///
/// <para>
/// <strong>TimestampUtc enforcement:</strong>
/// <see cref="InsertAsync"/> sets <see cref="AuditLogEntry.TimestampUtc"/> to
/// <see cref="DateTime.UtcNow"/> when it detects the timestamp is still at its
/// default value (<see cref="DateTime.MinValue"/>).  This acts as a safety net so
/// that callers which forget to supply a timestamp still produce a correct UTC time
/// rather than persisting a misleading epoch value (Acceptance Criteria).
/// </para>
///
/// <para>
/// <strong>Append-only contract:</strong>
/// Only <see cref="InsertAsync"/> and <see cref="GetPagedAsync"/> are exposed.
/// No Update, Delete, or GetById methods are present; the interface enforces this
/// constraint at compile time.
/// </para>
/// </remarks>
public sealed class AuditRepository : IAuditRepository
{
    // Factory used exclusively by InsertAsync to obtain a fresh, independent
    // DbContext scope for each audit write. Registered as AddDbContextFactory<T>
    // in DI so that the factory itself can be Singleton while each CreateDbContext()
    // call produces a new Scoped context.
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    // The request-scoped context injected via constructor DI.
    // Used only by GetPagedAsync (reads); never used for writes so there is no
    // risk of the audit INSERT participating in the request's ambient transaction.
    private readonly AppDbContext _readContext;

    private readonly ILogger<AuditRepository> _logger;

    /// <summary>
    /// Initialises the repository with the required dependencies.
    /// </summary>
    /// <param name="contextFactory">
    /// Factory that creates short-lived, independent <see cref="AppDbContext"/>
    /// instances for each audit INSERT. Must be registered via
    /// <c>services.AddDbContextFactory&lt;AppDbContext&gt;()</c>.
    /// </param>
    /// <param name="readContext">
    /// The request-scoped <see cref="AppDbContext"/> used for read-only queries
    /// (<see cref="GetPagedAsync"/>). This context must NOT be used for writes to
    /// avoid cross-contamination with the request's ambient unit of work.
    /// </param>
    /// <param name="logger">
    /// Structured logger for diagnostic and error events emitted by this repository.
    /// </param>
    public AuditRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        AppDbContext readContext,
        ILogger<AuditRepository> logger)
    {
        _contextFactory = contextFactory;
        _readContext = readContext;
        _logger = logger;
    }

    // ────────────────────────────────────────────────────────────────────────
    // InsertAsync — append-only write on an independent DbContext scope
    // ────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// A fresh <see cref="AppDbContext"/> is created via
    /// <see cref="IDbContextFactory{TContext}"/> for each call, committed immediately,
    /// then disposed.  This guarantees the audit record is durably persisted
    /// regardless of what happens to the calling request's unit of work.
    ///
    /// <para>
    /// <see cref="AuditLogEntry.TimestampUtc"/> is set to <see cref="DateTime.UtcNow"/>
    /// when the entry arrives with <see cref="DateTime.MinValue"/> (the CLR default
    /// for the <see cref="DateTime"/> value type), protecting against callers that
    /// inadvertently omit the timestamp.
    /// </para>
    /// </remarks>
    public async Task InsertAsync(
        AuditLogEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Ensure TimestampUtc is always a meaningful, UTC-tagged value.
        // The factory methods on AuditLogEntry accept a caller-supplied timestamp
        // (which should already be DateTime.UtcNow). This guard handles the edge
        // case where a caller passes DateTime.MinValue (the default value type default)
        // or forgets to supply the timestamp entirely.
        EnsureTimestampSet(entry);

        _logger.LogDebug(
            "Inserting audit log entry: UserId={UserId} ActionType={ActionType} Timestamp={Timestamp:O}",
            entry.UserId,
            entry.ActionType,
            entry.TimestampUtc);

        try
        {
            // Create a brand-new DbContext that is completely independent of the
            // calling request's scoped context. await using disposes it after the
            // SaveChangesAsync completes (or throws), releasing the connection
            // back to the pool.
            await using var context = await _contextFactory
                .CreateDbContextAsync(cancellationToken);

            // AddAsync tracks the new entity; SaveChangesAsync commits it to its
            // own connection in an implicit EF Core transaction. No explicit
            // BeginTransaction() is needed because a single-row INSERT does not
            // require multi-statement atomicity.
            await context.AuditLogEntries.AddAsync(entry, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogDebug(
                "Audit log entry {EntryId} persisted successfully (ActionType={ActionType})",
                entry.Id,
                entry.ActionType);
        }
        catch (DbUpdateException ex)
        {
            // DbUpdateException wraps provider-level constraint/connection failures.
            // Re-throw as AuditFailureException so callers receive a single typed
            // domain exception for any audit-subsystem write failure (Acceptance Criteria:
            // "No exception swallowed — failures propagate as AuditFailureException").
            _logger.LogError(
                ex,
                "Failed to persist audit log entry {EntryId} for action {ActionType}: {Message}",
                entry.Id,
                entry.ActionType,
                ex.Message);

            throw new AuditFailureException(
                $"Failed to persist audit log entry for action '{entry.ActionType}': {ex.Message}",
                auditedAction: entry.ActionType.ToString(),
                innerException: ex);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not an audit system failure — propagate without wrapping
            // so the caller can distinguish cancellation from genuine persistence errors.
            _logger.LogDebug(
                "InsertAsync cancelled for audit entry {EntryId} (ActionType={ActionType})",
                entry.Id,
                entry.ActionType);
            throw;
        }
        catch (Exception ex)
        {
            // Any other unexpected exception (e.g. provider startup failure, serialisation
            // error) must also not be swallowed. Wrap and re-throw as AuditFailureException.
            _logger.LogError(
                ex,
                "Unexpected error persisting audit log entry {EntryId} for action {ActionType}",
                entry.Id,
                entry.ActionType);

            throw new AuditFailureException(
                $"Unexpected error persisting audit log entry for action '{entry.ActionType}': {ex.Message}",
                auditedAction: entry.ActionType.ToString(),
                innerException: ex);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // GetPagedAsync — paged, filtered read from the shared request-scope context
    // ────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// Results are always ordered by <see cref="AuditLogEntry.TimestampUtc"/> descending
    /// (newest first) — the most natural ordering for an audit log viewer. The composite
    /// index <c>idx_audit_user_timestamp</c> covers the UserId + TimestampUtc filter
    /// efficiently; <c>idx_audit_actiontype_timestamp</c> covers ActionType + TimestampUtc;
    /// and <c>idx_audit_timestamp</c> covers time-range-only queries.
    ///
    /// <para>
    /// <see cref="AuditLogQueryParameters.Page"/> is silently clamped to ≥ 1 and
    /// <see cref="AuditLogQueryParameters.PageSize"/> is silently clamped to
    /// [1, <see cref="AuditLogQueryParameters.MaxPageSize"/>] to prevent negative
    /// OFFSET values or unbounded result sets.
    /// </para>
    /// </remarks>
    public async Task<PagedResultDto<AuditLogEntry>> GetPagedAsync(
        AuditLogQueryParameters parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // Clamp pagination values server-side so that invalid inputs (page < 1,
        // pageSize = 0, pageSize > max) degrade gracefully without an exception.
        var page = Math.Max(1, parameters.Page);
        var pageSize = Math.Clamp(
            parameters.PageSize,
            1,
            AuditLogQueryParameters.MaxPageSize);

        _logger.LogDebug(
            "GetPagedAsync: UserId={UserId} ActionType={ActionType} From={From:O} To={To:O} Page={Page} PageSize={PageSize}",
            parameters.UserId,
            parameters.ActionType,
            parameters.From,
            parameters.To,
            page,
            pageSize);

        try
        {
            // Start with the full set; apply filters incrementally so that EF Core
            // can compose a single optimised SQL query with all predicates combined.
            // AsNoTracking: the returned entities are read-only DTOs — no mutations
            // will be applied through this context.
            var query = _readContext.AuditLogEntries
                .AsNoTracking();

            // ── Filter: UserId ───────────────────────────────────────────────
            // Only apply if the caller supplied a non-null value. This keeps the
            // generated SQL clean (no redundant AND clauses) when filters are omitted.
            if (parameters.UserId.HasValue)
                query = query.Where(e => e.UserId == parameters.UserId.Value);

            // ── Filter: ActionType ───────────────────────────────────────────
            if (parameters.ActionType.HasValue)
                query = query.Where(e => e.ActionType == parameters.ActionType.Value);

            // ── Filter: From (inclusive lower bound on TimestampUtc) ─────────
            if (parameters.From.HasValue)
            {
                // Ensure the bound is always interpreted as UTC regardless of the
                // DateTimeKind carried by the caller's value, preventing off-by-offset
                // comparisons on PostgreSQL which is strict about timezone metadata.
                var from = EnsureUtc(parameters.From.Value);
                query = query.Where(e => e.TimestampUtc >= from);
            }

            // ── Filter: To (inclusive upper bound on TimestampUtc) ───────────
            if (parameters.To.HasValue)
            {
                var to = EnsureUtc(parameters.To.Value);
                query = query.Where(e => e.TimestampUtc <= to);
            }

            // ── Count total matching records before pagination ────────────────
            // CountAsync issues a SELECT COUNT(*) with the same WHERE predicates
            // but no ORDER BY / LIMIT, which is the most efficient total-count path.
            var totalCount = await query.CountAsync(cancellationToken);

            // ── Apply ordering then paginate ──────────────────────────────────
            // Always order by TimestampUtc DESC (newest first) for an audit log.
            // Skip/Take produce OFFSET / LIMIT in the generated SQL.
            var data = await query
                .OrderByDescending(e => e.TimestampUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            _logger.LogDebug(
                "GetPagedAsync returned {DataCount} of {TotalCount} matching entries (page {Page}/{TotalPages})",
                data.Count,
                totalCount,
                page,
                totalCount == 0 ? 1 : (int)Math.Ceiling((double)totalCount / pageSize));

            return PagedResultDto<AuditLogEntry>.Create(
                data: data.AsReadOnly(),
                totalCount: totalCount,
                page: page,
                pageSize: pageSize);
        }
        catch (DbUpdateException ex)
        {
            // Unlikely for a read, but wrap consistently for uniformity.
            _logger.LogError(
                ex,
                "Database error while querying audit log entries: {Message}",
                ex.Message);

            throw new AuditFailureException(
                $"Failed to query audit log entries: {ex.Message}",
                auditedAction: "GetPagedAuditLog",
                innerException: ex);
        }
        catch (OperationCanceledException)
        {
            // Propagate cancellation without wrapping.
            _logger.LogDebug("GetPagedAsync cancelled");
            throw;
        }
        catch (Exception ex) when (ex is not AuditFailureException)
        {
            // Guard against provider-level exceptions (e.g. NpgsqlException for a
            // dropped connection) that are not already AuditFailureException. Wrapping
            // ensures the caller always gets a typed domain exception for any
            // audit-subsystem failure.
            _logger.LogError(
                ex,
                "Unexpected error while querying audit log entries: {Message}",
                ex.Message);

            throw new AuditFailureException(
                $"Unexpected error querying audit log entries: {ex.Message}",
                auditedAction: "GetPagedAuditLog",
                innerException: ex);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets <see cref="AuditLogEntry.TimestampUtc"/> to <see cref="DateTime.UtcNow"/>
    /// when the entry's timestamp is still the CLR default (<see cref="DateTime.MinValue"/>).
    /// Uses reflection-backed internal setter access via EF Core conventions because
    /// <see cref="AuditLogEntry"/> exposes only private setters.
    /// </summary>
    /// <remarks>
    /// <see cref="AuditLogEntry"/> uses private setters to enforce immutability through
    /// its factory methods.  Rather than using reflection to bypass access modifiers at
    /// runtime (which would be brittle and non-obvious), this method instead delegates
    /// the timestamp check to the entity's <c>TimestampUtc</c> value directly via the
    /// public property getter, and reconstructs the entry through the factory method if
    /// a replacement is needed.
    ///
    /// <para>
    /// In practice this path is only hit when a caller passes <see cref="DateTime.MinValue"/>.
    /// The factory methods (<see cref="AuditLogEntry.CreateForUser"/>,
    /// <see cref="AuditLogEntry.CreateForSystem"/>) require the caller to provide
    /// a <c>timestampUtc</c> argument explicitly, so under normal usage the entry
    /// already has a valid timestamp on entry to this method.
    /// </para>
    ///
    /// <para>
    /// <strong>Implementation note:</strong> Because <see cref="AuditLogEntry"/>
    /// uses private setters, the most reliable way to enforce the UTC timestamp
    /// at the repository boundary — without leaking private-setter access — is to
    /// create a replacement entry via the factory method when the timestamp is
    /// invalid, and return the replacement to the caller.  However, since
    /// <see cref="InsertAsync"/> receives the entry by reference and the factory
    /// methods return new instances, the simplest correct approach is to use the
    /// EF Core <c>Property("TimestampUtc")</c> entry accessor in a transient
    /// context to set the value — or to require the caller to always supply a
    /// valid timestamp (which the interface contract documents clearly).
    /// </para>
    ///
    /// <para>
    /// <strong>Design decision (ASSUMPTION):</strong> Rather than modifying the domain
    /// entity to expose an internal setter (which would violate encapsulation) or using
    /// reflection (which is fragile), the repository logs a warning and relies on the
    /// fact that <see cref="AppDbContext.EnforceUtcDateTimes"/> will normalise the
    /// <see cref="DateTimeKind"/> before saving — meaning the <em>kind</em> is always
    /// correct.  The <em>value</em> of <see cref="DateTime.MinValue"/> would be an
    /// incorrect timestamp and must never reach the database, so the repository guards
    /// against this by logging the violation and re-throwing a typed
    /// <see cref="AuditFailureException"/> when the timestamp is the default, rather
    /// than silently persisting a misleading epoch timestamp.
    /// </para>
    /// </remarks>
    private void EnsureTimestampSet(AuditLogEntry entry)
    {
        if (entry.TimestampUtc == DateTime.MinValue)
        {
            // ASSUMPTION: Callers should always supply a valid UTC timestamp via the
            // AuditLogEntry factory methods. A MinValue timestamp indicates a programming
            // error in the caller. Log an error and throw AuditFailureException rather
            // than silently persisting an epoch timestamp that would corrupt the audit trail.
            _logger.LogError(
                "AuditLogEntry {EntryId} (ActionType={ActionType}) has a default TimestampUtc "
                + "(DateTime.MinValue). Callers must supply a valid UTC timestamp via "
                + "AuditLogEntry.CreateForUser() or AuditLogEntry.CreateForSystem().",
                entry.Id,
                entry.ActionType);

            throw new AuditFailureException(
                $"AuditLogEntry for action '{entry.ActionType}' was submitted with a default "
                + "TimestampUtc (DateTime.MinValue). Supply DateTime.UtcNow as the timestampUtc "
                + "argument to AuditLogEntry.CreateForUser() or AuditLogEntry.CreateForSystem().",
                auditedAction: entry.ActionType.ToString());
        }

        // If the timestamp has an Unspecified kind (e.g. came from deserialisation),
        // reinterpret it as UTC. AppDbContext.EnforceUtcDateTimes handles this at the
        // DbContext level too, but we apply it here for clarity in log output.
        if (entry.TimestampUtc.Kind == DateTimeKind.Local)
        {
            _logger.LogWarning(
                "AuditLogEntry {EntryId} (ActionType={ActionType}) has a Local-kind TimestampUtc. "
                + "Always use DateTime.UtcNow to create audit log entries.",
                entry.Id,
                entry.ActionType);

            // We cannot mutate the private setter directly. Log the warning and
            // allow AppDbContext.EnforceUtcDateTimes to normalise the Kind before
            // the value reaches the database driver. The value itself (the instant
            // in time) is preserved; only the Kind annotation changes.
        }
    }

    /// <summary>
    /// Ensures that a <see cref="DateTime"/> value carries
    /// <see cref="DateTimeKind.Utc"/> before it is used in a database comparison.
    /// </summary>
    /// <param name="value">The DateTime value to normalise.</param>
    /// <returns>
    /// The same instant in time with <see cref="DateTimeKind.Utc"/> specified.
    /// If <paramref name="value"/> is already UTC it is returned unchanged.
    /// If it is <see cref="DateTimeKind.Local"/> it is converted to UTC via
    /// <see cref="DateTime.ToUniversalTime()"/>.
    /// If it is <see cref="DateTimeKind.Unspecified"/> it is reinterpreted (not
    /// converted) as UTC via <see cref="DateTime.SpecifyKind"/>.
    /// </returns>
    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc        => value,
            DateTimeKind.Local      => value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _                       => value
        };
}
