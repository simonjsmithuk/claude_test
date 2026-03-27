namespace DataViewer.Domain.Exceptions;

/// <summary>
/// Thrown when an attempt is made to create or rename a resource using a name that
/// is already in use by another non-deleted entity of the same type.
/// </summary>
/// <remarks>
/// Maps to <c>HTTP 409 Conflict</c> in the API layer.
/// <para>
/// This exception is raised exclusively by the persistence layer before issuing an
/// <c>INSERT</c> or <c>UPDATE</c> when the uniqueness check against non-deleted records
/// would fail.  The early check avoids relying solely on the database unique index
/// violation (which would surface as a provider-specific <see cref="DbUpdateException"/>)
/// and instead surfaces a clean, typed domain exception with a human-readable message
/// that callers can handle without inspecting raw SQL error codes.
/// </para>
/// <para>
/// Typical <see cref="ResourceType"/> values: <c>"CredentialProfile"</c>.
/// </para>
/// </remarks>
public sealed class DuplicateNameException : DomainException
{
    /// <summary>
    /// Initialises the exception for a resource whose name conflicts with an existing
    /// non-deleted entity.
    /// </summary>
    /// <param name="resourceType">
    /// Short, human-readable label for the type of resource whose name is duplicated
    /// (e.g. <c>"CredentialProfile"</c>).
    /// </param>
    /// <param name="name">
    /// The name value that already exists in the backing store.
    /// </param>
    public DuplicateNameException(string resourceType, string name)
        : base(BuildMessage(resourceType, name))
    {
        ResourceType = resourceType;
        Name = name;
    }

    // ── Resource-identification properties ───────────────────────────────────

    /// <summary>
    /// Short, human-readable label for the type of resource whose name collided
    /// (e.g. <c>"CredentialProfile"</c>).
    /// </summary>
    public string ResourceType { get; }

    /// <summary>
    /// The duplicate name value that triggered the conflict.
    /// </summary>
    public string Name { get; }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static string BuildMessage(string resourceType, string name) =>
        $"A {resourceType} with the name '{name}' already exists.";
}
