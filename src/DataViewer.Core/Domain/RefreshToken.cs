// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  RETIRED — DataViewer.Core.Domain.RefreshToken                          ║
// ║                                                                          ║
// ║  This file is intentionally empty. The canonical RefreshToken entity    ║
// ║  now lives in DataViewer.Domain.Entities.RefreshToken (Guid PK/FK).     ║
// ║                                                                          ║
// ║  Migration notes (GAP-1 fix — TASK-002 code review):                    ║
// ║  • Old PK type:  int  → New: Guid                                        ║
// ║  • Old FK type:  int  → New: Guid (UserId)                               ║
// ║                                                                          ║
// ║  All code referencing DataViewer.Core.Domain.RefreshToken must be       ║
// ║  updated to reference DataViewer.Domain.Entities.RefreshToken.          ║
// ╚══════════════════════════════════════════════════════════════════════════╝
