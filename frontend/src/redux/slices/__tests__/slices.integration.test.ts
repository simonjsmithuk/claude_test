/**
 * slices.integration.test.ts
 * ==========================
 * Integration tests that exercise all six Redux slices together through
 * a real Redux store — verifying that cross-slice dispatches, selector
 * compositions, and a complete user journey (login → use → logout) work
 * correctly without mocking the store itself.
 *
 * User stories covered:
 *  - US-001: Authenticated user can log in and see their token persisted
 *  - US-002: Viewer searches transactions and selects a detail record
 *  - US-003: Admin views the audit log with filters applied
 *  - US-004: User updates preferences; other slices see the change
 *  - US-005: Logout clears all sensitive state across every slice
 *  - US-006: Global UI messages are raised and dismissed correctly
 */

import { configureStore } from '@reduxjs/toolkit';
import authReducer, {
  setCredentials,
  clearCredentials,
  selectIsAuthenticated,
  selectCurrentUser,
  selectAccessToken,
} from '../authSlice';
import credentialProfilesReducer, {
  setProfiles,
  upsertProfile,
  removeProfile,
  setActiveProfileId,
  setTestConnectionPending,
  setTestConnectionResult,
  clearProfiles,
  selectAllProfiles,
  selectActiveProfile,
  selectTestConnectionStatus,
} from '../credentialProfilesSlice';
import transactionsReducer, {
  setSearchFilters,
  mergeSearchFilters,
  setSearchResults,
  setSelectedTransaction,
  clearTransactions,
  selectTransactionResults,
  selectSelectedTransaction,
  selectSearchFilters,
} from '../transactionsSlice';
import preferencesReducer, {
  setPreferences,
  patchPreferences,
  clearPreferences,
  selectPreferences,
  selectDefaultPageSize,
  selectPreferredProfileId,
} from '../preferencesSlice';
import auditReducer, {
  setAuditEntries,
  setAuditFilters,
  clearAudit,
  selectAuditEntries,
  selectAuditQueryParams,
} from '../auditSlice';
import uiReducer, {
  setLoading,
  setError,
  setSuccess,
  clearMessages,
  resetUi,
  selectIsLoading,
  selectHasNotification,
  selectErrorMessage,
  selectSuccessMessage,
} from '../uiSlice';

import type {
  CredentialProfileDto,
  TransactionSummaryDto,
  TransactionDetailDto,
  AuditLogEntryDto,
  UserPreferenceDto,
  TestConnectionResult,
} from '../../../types/api.types';

// ---------------------------------------------------------------------------
// Build a lightweight test store (no RTK Query API slice needed)
// ---------------------------------------------------------------------------

function buildTestStore() {
  return configureStore({
    reducer: {
      auth: authReducer,
      credentialProfiles: credentialProfilesReducer,
      transactions: transactionsReducer,
      preferences: preferencesReducer,
      audit: auditReducer,
      ui: uiReducer,
    },
  });
}

type TestStore = ReturnType<typeof buildTestStore>;
type TestRootState = ReturnType<TestStore['getState']>;

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

function makeProfile(id: string, isActive = false): CredentialProfileDto {
  return {
    id,
    name: `Profile ${id}`,
    accessKeyId: `AKIA${id.toUpperCase()}`,
    region: 'eu-west-1',
    bucketName: 'transactions-bucket',
    keyPrefix: null,
    isActive,
    isDeleted: false,
    createdAt: '2025-01-01T00:00:00Z',
    updatedAt: '2025-01-01T00:00:00Z',
  };
}

function makeSummary(s3Key: string, statusCode = 200): TransactionSummaryDto {
  return {
    s3Key,
    method: 'GET',
    statusCode,
    urlPath: `/api/resource/${s3Key}`,
    timestampUtc: '2025-07-10T08:00:00Z',
    fileSizeBytes: 2048,
  };
}

function makeDetail(s3Key: string): TransactionDetailDto {
  return {
    metadata: {
      s3Key,
      compressedSizeBytes: 256,
      decompressedSizeBytes: 512,
      timestampUtc: '2025-07-10T08:00:00Z',
      s3LastModified: '2025-07-10T08:01:00Z',
    },
    request: {
      method: 'GET',
      urlPath: '/api/data',
      httpVersion: 'HTTP/1.1',
      headers: { Accept: 'application/json' },
      body: null,
      bodyContentType: null,
      isBodyTruncated: false,
    },
    response: {
      httpVersion: 'HTTP/1.1',
      statusCode: 200,
      statusText: 'OK',
      headers: { 'Content-Type': 'application/json' },
      body: '{"data":"value"}',
      bodyContentType: 'json',
      isBodyTruncated: false,
    },
  };
}

function makeAuditEntry(id: string): AuditLogEntryDto {
  return {
    id,
    userId: 'usr-admin',
    userName: 'adminUser',
    actionType: 'SearchTransactions',
    timestampUtc: '2025-07-10T09:00:00Z',
    ipAddress: '10.0.0.1',
    parameters: { method: 'GET', statusClass: '2xx' },
    resultCount: 42,
    s3ObjectKey: null,
  };
}

const preferences: UserPreferenceDto = {
  defaultPageSize: 50,
  defaultDateRangeDays: 14,
  preferredProfileId: 'prof-A',
};

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('Redux slices integration', () => {
  let store: TestStore;

  beforeEach(() => {
    sessionStorage.clear();
    store = buildTestStore();
  });

  // =========================================================================
  // US-001 — Login flow
  // =========================================================================

  describe('US-001: Login flow', () => {
    it('starts fully unauthenticated', () => {
      const state: TestRootState = store.getState();

      expect(selectIsAuthenticated(state as any)).toBe(false);
      expect(selectCurrentUser(state as any)).toBeNull();
      expect(selectAccessToken(state as any)).toBeNull();
    });

    it('marks the user as authenticated after setCredentials', () => {
      store.dispatch(
        setCredentials({
          accessToken: 'tok-abc',
          refreshToken: 'ref-xyz',
          expiresAt: '2025-07-10T13:00:00Z',
          user: { id: 'usr-1', username: 'alice', role: 'Admin' },
        }),
      );

      const state: TestRootState = store.getState();
      expect(selectIsAuthenticated(state as any)).toBe(true);
      expect(selectCurrentUser(state as any)?.username).toBe('alice');
    });

    it('persists the access token to sessionStorage on setCredentials', () => {
      store.dispatch(
        setCredentials({
          accessToken: 'integration-token',
          refreshToken: 'integration-refresh',
          expiresAt: '2025-07-10T13:00:00Z',
          user: { id: 'usr-1', username: 'alice', role: 'Admin' },
        }),
      );

      expect(sessionStorage.getItem('dv_access_token')).toBe('integration-token');
      expect(sessionStorage.getItem('dv_refresh_token')).toBe('integration-refresh');
    });
  });

  // =========================================================================
  // US-002 — Transaction search and detail view
  // =========================================================================

  describe('US-002: Transaction search and detail view', () => {
    it('applies search filters and stores results from a search', () => {
      // 1. Set initial filters.
      store.dispatch(setSearchFilters({ method: 'GET', pageSize: 20, page: 1 }));

      // 2. Simulate results arriving from an API call.
      const results = [makeSummary('key-1'), makeSummary('key-2', 404)];
      store.dispatch(setSearchResults({ results, totalCount: 2, page: 1 }));

      const state: TestRootState = store.getState();

      expect(selectTransactionResults(state as any)).toHaveLength(2);
      expect(selectSearchFilters(state as any).method).toBe('GET');
    });

    it('merges an additional filter without losing the first', () => {
      store.dispatch(setSearchFilters({ method: 'POST', pageSize: 20, page: 1 }));
      store.dispatch(mergeSearchFilters({ statusCode: '500' }));

      const filters = selectSearchFilters(store.getState() as any);

      expect(filters.method).toBe('POST');
      expect(filters.statusCode).toBe('500');
    });

    it('opens and closes a transaction detail record', () => {
      const detail = makeDetail('logs/key-1.gz');
      store.dispatch(setSelectedTransaction(detail));
      expect(
        selectSelectedTransaction(store.getState() as any)?.metadata.s3Key,
      ).toBe('logs/key-1.gz');

      store.dispatch(setSelectedTransaction(null));
      expect(selectSelectedTransaction(store.getState() as any)).toBeNull();
    });

    it('resets results on clearTransactions', () => {
      store.dispatch(
        setSearchResults({
          results: [makeSummary('key-1')],
          totalCount: 1,
          page: 1,
        }),
      );
      store.dispatch(setSelectedTransaction(makeDetail('logs/key-1.gz')));

      store.dispatch(clearTransactions());

      const state: TestRootState = store.getState();
      expect(selectTransactionResults(state as any)).toEqual([]);
      expect(selectSelectedTransaction(state as any)).toBeNull();
    });
  });

  // =========================================================================
  // US-003 — Admin audit log with filters
  // =========================================================================

  describe('US-003: Admin audit log view', () => {
    it('stores audit entries and derives query params correctly', () => {
      const entries = [makeAuditEntry('e-1'), makeAuditEntry('e-2')];
      store.dispatch(setAuditEntries({ entries, totalCount: 50, page: 1 }));
      store.dispatch(
        setAuditFilters({
          userId: 'usr-admin',
          actionType: 'SearchTransactions',
          fromDate: '2025-07-01T00:00:00Z',
        }),
      );

      const state: TestRootState = store.getState();

      expect(selectAuditEntries(state as any)).toHaveLength(2);

      const params = selectAuditQueryParams(state as any);
      expect(params.userId).toBe('usr-admin');
      expect(params.actionType).toBe('SearchTransactions');
      expect(params.fromDate).toBe('2025-07-01T00:00:00Z');
      expect(params.page).toBe(1);
      expect(params.pageSize).toBe(25);
    });

    it('clears audit state on logout', () => {
      store.dispatch(
        setAuditEntries({ entries: [makeAuditEntry('e-1')], totalCount: 1, page: 1 }),
      );
      store.dispatch(clearAudit());

      expect(selectAuditEntries(store.getState() as any)).toEqual([]);
    });
  });

  // =========================================================================
  // US-004 — User preferences influence other slices
  // =========================================================================

  describe('US-004: Preferences drive other slice defaults', () => {
    it('selectDefaultPageSize reflects stored preferences', () => {
      store.dispatch(setPreferences(preferences));

      expect(selectDefaultPageSize(store.getState() as any)).toBe(50);
    });

    it('selectPreferredProfileId reflects stored preferences', () => {
      store.dispatch(setPreferences(preferences));

      expect(selectPreferredProfileId(store.getState() as any)).toBe('prof-A');
    });

    it('patchPreferences only changes specified fields', () => {
      store.dispatch(setPreferences(preferences));
      store.dispatch(patchPreferences({ defaultPageSize: 25 }));

      const prefs = selectPreferences(store.getState() as any);
      expect(prefs?.defaultPageSize).toBe(25);
      expect(prefs?.defaultDateRangeDays).toBe(14); // unchanged
      expect(prefs?.preferredProfileId).toBe('prof-A'); // unchanged
    });
  });

  // =========================================================================
  // US-005 — Logout clears all sensitive state
  // =========================================================================

  describe('US-005: Full logout flow clears sensitive data', () => {
    beforeEach(() => {
      // Simulate a fully logged-in, active session.
      store.dispatch(
        setCredentials({
          accessToken: 'tok-session',
          refreshToken: 'ref-session',
          expiresAt: '2025-07-10T14:00:00Z',
          user: { id: 'usr-1', username: 'alice', role: 'Admin' },
        }),
      );
      store.dispatch(setProfiles([makeProfile('prof-A', true), makeProfile('prof-B')]));
      store.dispatch(
        setSearchResults({
          results: [makeSummary('k-1'), makeSummary('k-2')],
          totalCount: 2,
          page: 1,
        }),
      );
      store.dispatch(setSelectedTransaction(makeDetail('k-1')));
      store.dispatch(setPreferences(preferences));
      store.dispatch(
        setAuditEntries({
          entries: [makeAuditEntry('e-1')],
          totalCount: 1,
          page: 1,
        }),
      );
    });

    it('dispatching clearCredentials marks user as unauthenticated', () => {
      store.dispatch(clearCredentials());

      expect(selectIsAuthenticated(store.getState() as any)).toBe(false);
      expect(selectCurrentUser(store.getState() as any)).toBeNull();
    });

    it('dispatching clearCredentials removes tokens from sessionStorage', () => {
      store.dispatch(clearCredentials());

      expect(sessionStorage.getItem('dv_access_token')).toBeNull();
      expect(sessionStorage.getItem('dv_refresh_token')).toBeNull();
    });

    it('full logout sequence clears all feature slices', () => {
      // Simulate what a logout thunk would dispatch.
      store.dispatch(clearCredentials());
      store.dispatch(clearProfiles());
      store.dispatch(clearTransactions());
      store.dispatch(clearPreferences());
      store.dispatch(clearAudit());
      store.dispatch(resetUi());

      const state: TestRootState = store.getState();

      expect(selectIsAuthenticated(state as any)).toBe(false);
      expect(selectAllProfiles(state as any)).toEqual([]);
      expect(selectTransactionResults(state as any)).toEqual([]);
      expect(selectSelectedTransaction(state as any)).toBeNull();
      expect(selectPreferences(state as any)).toBeNull();
      expect(selectAuditEntries(state as any)).toEqual([]);
      expect(selectIsLoading(state as any)).toBe(false);
      expect(selectHasNotification(state as any)).toBe(false);
    });
  });

  // =========================================================================
  // US-006 — Global UI notifications lifecycle
  // =========================================================================

  describe('US-006: Global UI notification lifecycle', () => {
    it('shows an error notification after setError', () => {
      store.dispatch(setError('Connection to S3 failed'));

      const state: TestRootState = store.getState();
      expect(selectHasNotification(state as any)).toBe(true);
      expect(selectErrorMessage(state as any)).toBe('Connection to S3 failed');
      expect(selectSuccessMessage(state as any)).toBeNull();
    });

    it('shows a success notification after setSuccess', () => {
      store.dispatch(setSuccess('Profile activated successfully'));

      const state: TestRootState = store.getState();
      expect(selectHasNotification(state as any)).toBe(true);
      expect(selectSuccessMessage(state as any)).toBe('Profile activated successfully');
      expect(selectErrorMessage(state as any)).toBeNull();
    });

    it('switching from error to success clears the error', () => {
      store.dispatch(setError('Oops'));
      store.dispatch(setSuccess('Fixed!'));

      const state: TestRootState = store.getState();
      expect(selectErrorMessage(state as any)).toBeNull();
      expect(selectSuccessMessage(state as any)).toBe('Fixed!');
    });

    it('clearMessages removes both messages', () => {
      store.dispatch(setSuccess('Done'));
      store.dispatch(clearMessages());

      expect(selectHasNotification(store.getState() as any)).toBe(false);
    });

    it('setLoading + setError combination works correctly', () => {
      // Typical pattern: start loading, fail, show error.
      store.dispatch(setLoading(true));
      store.dispatch(setError('API timeout'));
      store.dispatch(setLoading(false));

      const state: TestRootState = store.getState();
      expect(selectIsLoading(state as any)).toBe(false);
      expect(selectErrorMessage(state as any)).toBe('API timeout');
    });
  });

  // =========================================================================
  // Credential profiles — full lifecycle
  // =========================================================================

  describe('Credential profiles full lifecycle', () => {
    it('loads profiles, activates one, tests connection, then removes it', () => {
      const profA = makeProfile('prof-A', false);
      const profB = makeProfile('prof-B', true);

      // 1. Load initial list.
      store.dispatch(setProfiles([profA, profB]));
      expect(selectActiveProfile(store.getState() as any)?.id).toBe('prof-B');

      // 2. Activate profile A instead.
      store.dispatch(setActiveProfileId('prof-A'));
      expect(selectActiveProfile(store.getState() as any)?.id).toBe('prof-A');

      // 3. Test connection for profile A.
      store.dispatch(setTestConnectionPending('prof-A'));
      expect(
        selectTestConnectionStatus('prof-A')(store.getState() as any).status,
      ).toBe('pending');

      const result: TestConnectionResult = {
        success: true,
        message: 'Connected',
        testedAt: '2025-07-10T10:00:00Z',
      };
      store.dispatch(setTestConnectionResult({ profileId: 'prof-A', result }));
      expect(
        selectTestConnectionStatus('prof-A')(store.getState() as any).status,
      ).toBe('success');

      // 4. Upsert an update to profile A.
      const updatedA = { ...profA, name: 'Updated Profile A', isActive: true };
      store.dispatch(upsertProfile(updatedA));
      const allProfiles = selectAllProfiles(store.getState() as any);
      expect(allProfiles.find((p) => p.id === 'prof-A')?.name).toBe(
        'Updated Profile A',
      );

      // 5. Remove profile A — should reset active.
      store.dispatch(removeProfile('prof-A'));
      expect(selectActiveProfile(store.getState() as any)).toBeUndefined();
      expect(
        selectTestConnectionStatus('prof-A')(store.getState() as any).status,
      ).toBe('idle'); // cleaned up
    });
  });

  // =========================================================================
  // Cross-slice consistency: preferences pageSize drives transaction search
  // =========================================================================

  describe('Cross-slice: preferences page size influences transaction search', () => {
    it('search filter picks up the user-preferred page size when applied manually', () => {
      // Typically, the component reads selectDefaultPageSize and applies it.
      store.dispatch(setPreferences({ defaultPageSize: 100, defaultDateRangeDays: 30, preferredProfileId: null }));

      const defaultPageSize = selectDefaultPageSize(store.getState() as any);
      store.dispatch(
        setSearchFilters({ pageSize: defaultPageSize, page: 1 }),
      );

      const filters = selectSearchFilters(store.getState() as any);
      expect(filters.pageSize).toBe(100);
    });
  });
});
