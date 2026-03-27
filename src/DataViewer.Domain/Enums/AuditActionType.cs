namespace DataViewer.Domain.Enums;

/// <summary>
/// Categorises every auditable action performed within DataViewer.
/// Integer values are persisted to the database — do not reorder or reassign.
/// </summary>
/// <remarks>
/// New members must always be appended with the next sequential integer value;
/// existing values must never be changed or removed to preserve historical records.
/// </remarks>
public enum AuditActionType
{
    // ── S3 data-access actions ───────────────────────────────────────────────

    /// <summary>User executed a metadata search against an S3 credential profile.</summary>
    SearchTransactions = 0,

    /// <summary>User retrieved the full detail of a single S3 transaction record.</summary>
    ViewTransaction = 1,

    // ── Credential profile management (Admin) ────────────────────────────────

    /// <summary>Admin created a new credential profile.</summary>
    CreateCredentialProfile = 2,

    /// <summary>Admin updated an existing credential profile.</summary>
    UpdateCredentialProfile = 3,

    /// <summary>Admin soft-deleted a credential profile.</summary>
    DeleteCredentialProfile = 4,

    /// <summary>Admin triggered an S3 connectivity test for a credential profile.</summary>
    TestCredentialProfile = 5,

    /// <summary>Admin set a credential profile as the active default.</summary>
    ActivateCredentialProfile = 6,

    // ── Authentication events ────────────────────────────────────────────────

    /// <summary>User successfully authenticated and received a token pair.</summary>
    Login = 7,

    /// <summary>User explicitly logged out, causing refresh-token revocation.</summary>
    Logout = 8,

    /// <summary>A login attempt failed due to bad credentials.</summary>
    LoginFailed = 9,

    /// <summary>An account was locked after exceeding the failed-login threshold.</summary>
    AccountLocked = 10,

    // ── User management (Admin) ──────────────────────────────────────────────

    /// <summary>
    /// Admin changed the <see cref="UserRole"/> of an existing user account.
    /// The <c>Parameters</c> field on the log entry should capture both the
    /// previous and new role values.
    /// </summary>
    UpdateUserRole = 11,

    /// <summary>
    /// Admin manually cleared the lockout state of a user account
    /// (i.e. called <see cref="DataViewer.Domain.Entities.User.Unlock"/>).
    /// </summary>
    UnlockAccount = 12,

    // ── System settings (Admin) ──────────────────────────────────────────────

    /// <summary>
    /// Admin updated one or more fields on the <see cref="DataViewer.Domain.Entities.SystemSettings"/>
    /// singleton row. The <c>Parameters</c> field on the log entry should capture a
    /// JSON diff of the changed settings values.
    /// </summary>
    UpdateSystemSettings = 13,
}
