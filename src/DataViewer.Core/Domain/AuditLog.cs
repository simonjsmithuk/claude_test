// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  RETIRED — DataViewer.Core.Domain.AuditLog                              ║
// ║                                                                          ║
// ║  This file is intentionally empty. The canonical audit log entity now   ║
// ║  lives in DataViewer.Domain.Entities.AuditLogEntry.                     ║
// ║                                                                          ║
// ║  Migration notes (GAP-1 fix — TASK-002 code review):                    ║
// ║  • Old PK type:  long   → New: Guid                                      ║
// ║  • Old FK type:  int    → New: Guid (UserId)                             ║
// ║  • Old field:    Action (string) → New: ActionType (AuditActionType enum)║
// ║  • Old field:    CreatedAt       → New: TimestampUtc                     ║
// ║  • Old field:    IpAddress (string?) preserved as string? in new entity  ║
// ║  • Added:        ResultCount, S3ObjectKey, ProfileName                   ║
// ║  • Removed:      User navigation property (by design — ADR-004)          ║
// ║                                                                          ║
// ║  The AuditActions static string constants are superseded by the          ║
// ║  DataViewer.Domain.Enums.AuditActionType enum.                           ║
// ║                                                                          ║
// ║  All code referencing DataViewer.Core.Domain.AuditLog must be updated   ║
// ║  to reference DataViewer.Domain.Entities.AuditLogEntry.                 ║
// ╚══════════════════════════════════════════════════════════════════════════╝
