using DataViewer.Domain.Entities;
using DataViewer.Domain.Enums;

namespace DataViewer.Tests.Auth.Helpers;

/// <summary>
/// Fluent test-data builder for <see cref="User"/> instances used in token tests.
/// </summary>
internal sealed class UserBuilder
{
    private Guid _id = Guid.NewGuid();
    private string _userName = "testuser";
    private string _email = "testuser@example.com";
    private UserRole _role = UserRole.Viewer;
    private string _passwordHash = "$2a$12$fakehash";

    // ── Fluent setters ────────────────────────────────────────────────────────

    internal UserBuilder WithId(Guid id)
    {
        _id = id;
        return this;
    }

    internal UserBuilder WithUserName(string userName)
    {
        _userName = userName;
        return this;
    }

    internal UserBuilder WithEmail(string email)
    {
        _email = email;
        return this;
    }

    internal UserBuilder WithRole(UserRole role)
    {
        _role = role;
        return this;
    }

    internal UserBuilder AsAdmin() => WithRole(UserRole.Admin);

    internal UserBuilder AsViewer() => WithRole(UserRole.Viewer);

    // ── Build ─────────────────────────────────────────────────────────────────

    internal User Build() => new()
    {
        Id = _id,
        UserName = _userName,
        Email = _email,
        PasswordHash = _passwordHash,
        Role = _role,
        CreatedAt = DateTime.UtcNow,
    };

    // ── Convenience factory methods ───────────────────────────────────────────

    internal static UserBuilder AViewer() =>
        new UserBuilder().WithRole(UserRole.Viewer);

    internal static UserBuilder AnAdmin() =>
        new UserBuilder().WithRole(UserRole.Admin);
}
