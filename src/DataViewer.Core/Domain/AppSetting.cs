// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  RETIRED — DataViewer.Core.Domain.AppSetting                            ║
// ║                                                                          ║
// ║  This file is intentionally empty. The generic key/value AppSetting     ║
// ║  model has been replaced by the strongly-typed singleton entity          ║
// ║  DataViewer.Domain.Entities.SystemSettings (int Id = 1).                ║
// ║                                                                          ║
// ║  Migration notes (GAP-1 fix — TASK-002 code review):                    ║
// ║  • SettingKeys.JwtAccessTokenLifetimeMinutes                             ║
// ║    → SystemSettings.JwtAccessTokenMinutes (int)                          ║
// ║  • SettingKeys.JwtRefreshTokenLifetimeHours                              ║
// ║    → SystemSettings.JwtRefreshTokenHours (int)                           ║
// ║  • SettingKeys.BodyMaxSizeBytes                                          ║
// ║    → SystemSettings.BodySizeCapMb (int, unit changed to MB)              ║
// ║  • SettingKeys.AccountLockoutThreshold                                   ║
// ║    → SystemSettings.LockoutThreshold (int)                               ║
// ║                                                                          ║
// ║  All code referencing DataViewer.Core.Domain.AppSetting or              ║
// ║  DataViewer.Core.Domain.SettingKeys must be updated to read settings     ║
// ║  from DataViewer.Domain.Entities.SystemSettings via the repository.     ║
// ╚══════════════════════════════════════════════════════════════════════════╝
