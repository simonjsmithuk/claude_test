#nullable enable

namespace DataViewer.Domain.Enums;

/// <summary>
/// Categorises every auditable action performed within DataViewer.
/// Integer values are persisted to the database — do not reorder or reassign.
/// </summary>
public enum AuditActionType
{
    /// <summary>User executed a metadata search against an S3 credential profile.</summary>
    SearchTransactions = 0,

    /// <summary>User retrieved the full detail of a single S3 transaction record.</summary>
    ViewTransaction = 1,

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

    /// <summary>User successfully authenticated and received a token pair.</summary>
    Login = 7,

    /// <summary>User explicitly logged out, causing refresh-token revocation.</summary>
    Logout = 8,

    /// <summary>A login attempt failed due to bad credentials.</summary>
    LoginFailed = 9,

    /// <summary>An account was locked after exceeding the failed-login threshold.</summary>
    AccountLocked = 10
}
