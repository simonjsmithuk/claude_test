/**
 * api.types.ts
 * ============
 * TypeScript interfaces for all DataViewer API DTOs.
 * These types mirror the backend API design (System Design §5) exactly.
 * No secrets (SecretAccessKey) ever appear in response types.
 */

// ---------------------------------------------------------------------------
// Shared / Pagination
// ---------------------------------------------------------------------------

/** Generic paginated envelope returned by list endpoints. */
export interface PagedResult<T> {
  data: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

// ---------------------------------------------------------------------------
// Auth (§5.2)
// ---------------------------------------------------------------------------

export interface LoginRequest {
  username: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
  /** ISO 8601 UTC timestamp */
  expiresAt: string;
  refreshToken: string;
}

export interface RefreshTokenRequest {
  refreshToken: string;
}

export interface LogoutRequest {
  refreshToken: string;
}

// ---------------------------------------------------------------------------
// Credential Profiles (§5.3)
// ---------------------------------------------------------------------------

/** Safe DTO — never includes secretAccessKey. */
export interface CredentialProfileDto {
  id: string;
  name: string;
  accessKeyId: string;
  region: string;
  bucketName: string;
  keyPrefix: string | null;
  isActive: boolean;
  isDeleted: boolean;
  /** ISO 8601 UTC */
  createdAt: string;
  /** ISO 8601 UTC */
  updatedAt: string;
}

export interface CreateCredentialProfileRequest {
  name: string;
  accessKeyId: string;
  /** Write-only — never returned in any response. */
  secretAccessKey: string;
  region: string;
  bucketName: string;
  keyPrefix?: string | null;
}

export interface UpdateCredentialProfileRequest {
  name?: string | null;
  accessKeyId?: string | null;
  /** Provide a new value to rotate the key; omit to keep existing. */
  secretAccessKey?: string | null;
  region?: string | null;
  bucketName?: string | null;
  keyPrefix?: string | null;
}

export interface TestConnectionResult {
  success: boolean;
  message: string;
  /** ISO 8601 UTC */
  testedAt: string;
}

export interface ActivateProfileResult {
  activeProfileId: string;
}

// ---------------------------------------------------------------------------
// Transactions (§5.4)
// ---------------------------------------------------------------------------

/** Summary row returned by the search endpoint. */
export interface TransactionSummaryDto {
  s3Key: string;
  method: string;
  statusCode: number;
  urlPath: string;
  /** ISO 8601 UTC */
  timestampUtc: string;
  fileSizeBytes: number;
}

/** Content-type hint for body rendering. */
export type BodyContentType = 'json' | 'xml' | 'text' | null;

export interface TransactionBodySection {
  /** HTTP method (request only) */
  method?: string;
  /** URL path (request only) */
  urlPath?: string;
  /** HTTP version string (e.g. "HTTP/1.1") */
  httpVersion: string;
  /** HTTP status code (response only) */
  statusCode?: number;
  /** HTTP status text (response only) */
  statusText?: string;
  headers: Record<string, string>;
  body: string | null;
  bodyContentType: BodyContentType;
  isBodyTruncated: boolean;
}

export interface TransactionMetadataDto {
  s3Key: string;
  compressedSizeBytes: number;
  decompressedSizeBytes: number;
  /** ISO 8601 UTC */
  timestampUtc: string;
  /** ISO 8601 UTC */
  s3LastModified: string;
}

export interface TransactionDetailDto {
  metadata: TransactionMetadataDto;
  request: TransactionBodySection;
  response: TransactionBodySection;
}

/** Query parameters for GET /api/v1/transactions */
export interface SearchTransactionsParams {
  /** ISO 8601 */
  fromDate?: string;
  /** ISO 8601 */
  toDate?: string;
  /** e.g. "200", "404" */
  statusCode?: string;
  /** e.g. "2xx", "4xx", "5xx" */
  statusClass?: string;
  /** GET | POST | PUT | DELETE | PATCH | HEAD | OPTIONS */
  method?: string;
  urlPrefix?: string;
  page?: number;
  pageSize?: number;
  /** Falls back to the active profile if omitted. */
  profileId?: string;
}

// ---------------------------------------------------------------------------
// User Preferences (§5.5)
// ---------------------------------------------------------------------------

export interface UserPreferenceDto {
  defaultPageSize: number;
  defaultDateRangeDays: number;
  preferredProfileId: string | null;
}

export interface UpdateUserPreferencesRequest {
  defaultPageSize?: number | null;
  defaultDateRangeDays?: number | null;
  preferredProfileId?: string | null;
}

// ---------------------------------------------------------------------------
// Admin Settings (§5.6)
// ---------------------------------------------------------------------------

export interface SystemSettingsDto {
  jwtAccessTokenMinutes: number;
  jwtRefreshTokenHours: number;
  bodySizeCapMb: number;
  lockoutThreshold: number;
}

export interface UpdateAdminSettingsRequest {
  jwtAccessTokenMinutes?: number | null;
  jwtRefreshTokenHours?: number | null;
  bodySizeCapMb?: number | null;
  lockoutThreshold?: number | null;
}

// ---------------------------------------------------------------------------
// Audit Logs (§5.7)
// ---------------------------------------------------------------------------

/** Action type string literals — mirrors AuditActionType backend enum (§5.8). */
export type AuditActionType =
  | 'SearchTransactions'
  | 'ViewTransaction'
  | 'CreateCredentialProfile'
  | 'UpdateCredentialProfile'
  | 'DeleteCredentialProfile'
  | 'TestCredentialProfile'
  | 'ActivateCredentialProfile'
  | 'Login'
  | 'Logout'
  | 'LoginFailed'
  | 'AccountLocked';

export interface AuditLogEntryDto {
  id: string;
  userId: string;
  userName: string;
  actionType: AuditActionType;
  /** ISO 8601 UTC */
  timestampUtc: string;
  ipAddress: string;
  /** Serialised filter/action parameters — structure varies by actionType. */
  parameters: Record<string, unknown> | null;
  resultCount: number | null;
  s3ObjectKey: string | null;
}

/** Query parameters for GET /api/v1/audit-logs */
export interface GetAuditLogsParams {
  /** ISO 8601 */
  fromDate?: string;
  /** ISO 8601 */
  toDate?: string;
  userId?: string;
  actionType?: AuditActionType;
  page?: number;
  pageSize?: number;
}

// ---------------------------------------------------------------------------
// RFC 7807 Problem Details (error shape from the API)
// ---------------------------------------------------------------------------

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
}
