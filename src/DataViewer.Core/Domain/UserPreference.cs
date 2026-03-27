// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  RETIRED — DataViewer.Core.Domain.UserPreference                        ║
// ║                                                                          ║
// ║  This file is intentionally empty. The canonical UserPreference entity  ║
// ║  now lives in DataViewer.Domain.Entities.UserPreference.                ║
// ║                                                                          ║
// ║  Migration notes (GAP-1 fix — TASK-002 code review):                    ║
// ║  • Old PK:   separate int Id + int UserId (two columns)                  ║
// ║              → New: UserId (Guid) is both PK and FK (shared PK pattern) ║
// ║  • Old field: DefaultPageSize default was 50 → New: 25 (spec-mandated)  ║
// ║  • Old field: DefaultDateRangeHours → New: DefaultDateRangeDays          ║
// ║  • Old field: PreferredProfileId (int?) → New: PreferredProfileId (Guid?)║
// ║  • Removed:  UpdatedAt (preferences updated at application layer)        ║
// ║  • Removed:  PreferredProfile navigation (use CredentialProfile directly)║
// ║                                                                          ║
// ║  All code referencing DataViewer.Core.Domain.UserPreference must be     ║
// ║  updated to reference DataViewer.Domain.Entities.UserPreference.        ║
// ╚══════════════════════════════════════════════════════════════════════════╝
