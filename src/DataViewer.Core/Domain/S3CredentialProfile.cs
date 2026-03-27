// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  RETIRED — DataViewer.Core.Domain.S3CredentialProfile                   ║
// ║                                                                          ║
// ║  This file is intentionally empty. The canonical credential profile     ║
// ║  entity now lives in DataViewer.Domain.Entities.CredentialProfile.      ║
// ║                                                                          ║
// ║  Migration notes (GAP-1 fix — TASK-002 code review):                    ║
// ║  • Old PK type:  int    → New: Guid                                      ║
// ║  • Old FK type:  int    → New: Guid (CreatedByUserId)                    ║
// ║  • Old field:    EncryptedSecretAccessKey (string)                       ║
// ║                  → New: EncryptedSecretKey (byte[]) — AES-256 bytes,     ║
// ║                         layout [16-byte IV] + [ciphertext]               ║
// ║  • Removed:      CreatedByUser navigation property (by design — ADR-004) ║
// ║                                                                          ║
// ║  All code referencing DataViewer.Core.Domain.S3CredentialProfile must   ║
// ║  be updated to reference DataViewer.Domain.Entities.CredentialProfile.  ║
// ╚══════════════════════════════════════════════════════════════════════════╝
