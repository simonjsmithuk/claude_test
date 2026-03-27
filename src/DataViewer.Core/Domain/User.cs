// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  RETIRED — DataViewer.Core.Domain.User                                  ║
// ║                                                                          ║
// ║  This file is intentionally empty. The canonical User entity now lives  ║
// ║  in DataViewer.Domain.Entities.User (Guid PK, UserRole enum).           ║
// ║                                                                          ║
// ║  Migration notes (GAP-1 fix — TASK-002 code review):                    ║
// ║  • Old PK type:  int          → New: Guid                               ║
// ║  • Old field:    Username     → New: UserName                            ║
// ║  • Old field:    FailedLoginAttempts → New: FailedLoginCount             ║
// ║  • Old field:    LockedAt     → New: LockoutUntil                        ║
// ║  • Old field:    Role (string)→ New: Role (UserRole enum)                ║
// ║  • Removed:      UpdatedAt (Users are not updated; fields are)           ║
// ║  • Removed:      IsActive (replaced by IsLocked + LockoutUntil)         ║
// ║  • Removed:      AuditLogs navigation (log entry has no User nav)        ║
// ║                                                                          ║
// ║  All code referencing DataViewer.Core.Domain.User must be updated to    ║
// ║  reference DataViewer.Domain.Entities.User.                             ║
// ╚══════════════════════════════════════════════════════════════════════════╝

// The Roles static class is superseded by DataViewer.Domain.Enums.UserRole.
// Delete usages of Roles.Admin / Roles.Viewer and replace with UserRole.Admin
// / UserRole.Viewer respectively.
