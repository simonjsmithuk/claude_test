using DataViewer.Application.Interfaces;
using DataViewer.Infrastructure.Encryption;
using Microsoft.Extensions.DependencyInjection;

namespace DataViewer.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods on <see cref="IServiceCollection"/> that register all
/// Infrastructure-layer services with the DI container.
/// </summary>
/// <remarks>
/// <para>
/// Call this method from the API project's composition root (<c>Program.cs</c>)
/// after <c>WebApplication.CreateBuilder(args)</c>:
/// <code>
/// builder.Services.AddInfrastructure();
/// </code>
/// </para>
/// <para>
/// The API project must never reference Infrastructure concrete types directly
/// (Clean Architecture rule). All service bindings are declared here and consumed
/// through the interfaces defined in <c>DataViewer.Application.Interfaces</c>.
/// </para>
/// </remarks>
public static class InfrastructureServiceExtensions
{
    /// <summary>
    /// Registers all Infrastructure-layer services into <paramref name="services"/>.
    /// </summary>
    /// <param name="services">The application's service collection.</param>
    /// <returns>
    /// The same <paramref name="services"/> instance to support method chaining.
    /// </returns>
    /// <remarks>
    /// <strong>Encryption service lifetime — Singleton:</strong>
    /// <see cref="AesEncryptionService"/> is registered as a Singleton because:
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       The AES key bytes are decoded from the environment variable once at
    ///       construction time and cached — re-reading on every request would be
    ///       wasteful and create an inconsistency window if the variable changes at runtime.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       All encryption/decryption operations are stateless beyond the immutable
    ///       key field, and <see cref="System.Security.Cryptography.AesGcm"/> instances
    ///       are created per-call rather than shared, making the Singleton safe for
    ///       concurrent use without locking.
    ///     </description>
    ///   </item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // ── Encryption ────────────────────────────────────────────────────────
        // AesEncryptionService reads DATAVIEWER_ENCRYPTION_KEY at construction time
        // and throws InvalidOperationException if the variable is missing or invalid.
        // With Singleton lifetime this validation runs exactly once — at startup —
        // ensuring the application fails fast with a clear error rather than
        // encountering a missing key mid-request.
        services.AddSingleton<IEncryptionService, AesEncryptionService>();

        return services;
    }
}
