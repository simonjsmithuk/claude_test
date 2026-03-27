using Microsoft.EntityFrameworkCore;

namespace DataViewer.Infrastructure.Persistence;

/// <summary>
/// A <see cref="IDbContextFactory{TContext}"/> implementation that creates
/// <see cref="AppDbContext"/> instances with the two-argument constructor
/// (<see cref="DbContextOptions{TContext}"/> + <c>databaseProvider</c> string).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AppDbContext"/> has a non-default constructor signature that requires
/// a <c>databaseProvider</c> string (e.g. <c>"postgresql"</c> or <c>"mysql"</c>)
/// in addition to the standard <see cref="DbContextOptions{TContext}"/>.
/// EF Core's built-in <c>PooledDbContextFactory</c> and the default factory registered
/// by <c>AddDbContextFactory</c> only supply <see cref="DbContextOptions{TContext}"/>,
/// so they cannot construct <see cref="AppDbContext"/> without the provider string.
/// </para>
///
/// <para>
/// This class bridges that gap: it is registered as the
/// <see cref="IDbContextFactory{TContext}"/> Singleton in
/// <c>InfrastructureServiceExtensions.ConfigureDbContext</c>, which replaces the
/// default EF Core factory descriptor with this implementation.
/// </para>
///
/// <para>
/// <strong>Usage:</strong> Inject <see cref="IDbContextFactory{AppDbContext}"/>
/// wherever an independent, short-lived <see cref="AppDbContext"/> is needed — most
/// notably in <c>AuditRepository.InsertAsync</c> to ensure audit INSERTs are committed
/// independently of the calling request's unit of work:
/// <code>
/// await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
/// await context.AuditLogEntries.AddAsync(entry, cancellationToken);
/// await context.SaveChangesAsync(cancellationToken);
/// </code>
/// The <c>await using</c> pattern disposes the context (and releases its connection
/// back to the connection pool) after the INSERT is committed, keeping connection
/// hold time minimal.
/// </para>
///
/// <para>
/// <strong>Thread safety:</strong> This factory is Singleton and may be called
/// concurrently. The <c>_options</c> and <c>_databaseProvider</c> fields are
/// read-only after construction and therefore safe for concurrent access without
/// any locking.
/// </para>
///
/// <para>
/// <strong>Context pooling:</strong> This implementation does NOT use
/// <c>PooledDbContextFactory</c>. Context pooling would interfere with the
/// two-argument constructor pattern and is not necessary for the low-frequency
/// audit INSERT workload.
/// </para>
/// </remarks>
internal sealed class AuditDbContextFactory : IDbContextFactory<AppDbContext>
{
    private readonly DbContextOptions<AppDbContext> _options;

    // Captured once at construction time from the DI-registered configuration value.
    // Safe to share across threads because it is immutable after construction.
    private readonly string _databaseProvider;

    /// <summary>
    /// Initialises the factory with the shared EF Core options and the active
    /// database provider name.
    /// </summary>
    /// <param name="options">
    /// The <see cref="DbContextOptions{TContext}"/> built by
    /// <see cref="DatabaseProviderFactory.Configure"/> and registered as a Singleton.
    /// Shared across all contexts created by this factory; EF Core's model cache
    /// ensures the compiled model is built only once.
    /// </param>
    /// <param name="databaseProvider">
    /// Normalised (lower-case) database provider identifier — <c>"postgresql"</c>
    /// or <c>"mysql"</c>. Forwarded to <see cref="AppDbContext"/> at construction
    /// time so that entity configurations emit the correct provider-specific column
    /// type annotations.
    /// </param>
    public AuditDbContextFactory(
        DbContextOptions<AppDbContext> options,
        string databaseProvider)
    {
        _options = options;
        _databaseProvider = databaseProvider;
    }

    /// <summary>
    /// Creates a new, independently-owned <see cref="AppDbContext"/> instance.
    /// </summary>
    /// <remarks>
    /// Each call produces a distinct context object with its own change tracker and
    /// database connection. The caller is responsible for disposing the returned
    /// context (the <c>await using</c> pattern is strongly recommended).
    /// </remarks>
    /// <returns>
    /// A new <see cref="AppDbContext"/> backed by the shared
    /// <see cref="DbContextOptions{TContext}"/> and configured with
    /// <see cref="_databaseProvider"/>.
    /// </returns>
    public AppDbContext CreateDbContext() =>
        new(_options, _databaseProvider);

    /// <summary>
    /// Creates a new, independently-owned <see cref="AppDbContext"/> instance
    /// asynchronously.
    /// </summary>
    /// <param name="cancellationToken">
    /// Token to observe for cooperative cancellation. Not used in this implementation
    /// because <see cref="AppDbContext"/> construction is synchronous, but included
    /// to satisfy the <see cref="IDbContextFactory{TContext}"/> contract and enable
    /// future async initialisation if needed.
    /// </param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that completes synchronously with a new
    /// <see cref="AppDbContext"/> instance.
    /// </returns>
    public ValueTask<AppDbContext> CreateDbContextAsync(
        CancellationToken cancellationToken = default)
    {
        // AppDbContext construction is synchronous; wrap in a completed ValueTask
        // to satisfy the async interface contract without unnecessary heap allocation
        // from Task.FromResult<T>().
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(CreateDbContext());
    }
}
