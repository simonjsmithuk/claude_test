#nullable enable

namespace DataViewer.Domain.Enums;

/// <summary>
/// Defines the access level of a user within the DataViewer application.
/// </summary>
/// <remarks>
/// Integer values are persisted to the database — do not reorder or reassign.
/// </remarks>
public enum UserRole
{
    /// <summary>Can search and view S3 transaction records. Cannot manage profiles or users.</summary>
    Viewer = 0,

    /// <summary>Full access: credential profiles, user management, and system settings.</summary>
    Admin = 1
}
