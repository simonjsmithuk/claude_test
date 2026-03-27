/**
 * auditSlice.test.ts
 * ==================
 * Unit tests for the audit Redux slice.
 *
 * Coverage targets:
 *  - Initial state shape
 *  - setAuditEntries: stores entries, totalCount, and page
 *  - setAuditFilters: replaces filters, resets to page 1
 *  - mergeAuditFilters: merges partial filters, resets to page 1
 *  - clearAuditFilters: empties filters, resets page
 *  - setAuditCurrentPage: updates page only
 *  - setAuditPageSize: updates pageSize, resets page 1
 *  - clearAudit: full entries/count/page/filters reset (preserves pageSize)
 *  - All selectors including selectAuditQueryParams
 *  - Edge cases: empty entry arrays, unknown actionType, boundary pages
 */

import auditReducer, {
  setAuditEntries,
  setAuditFilters,
  mergeAuditFilters,
  clearAuditFilters,
  setAuditCurrentPage,
  setAuditPageSize,
  clearAudit,
  selectAuditEntries,
  selectAuditTotalCount,
  selectAuditCurrentPage,
  selectAuditFilters,
  selectAuditPageSize,
  selectAuditQueryParams,
  type AuditState,
  type AuditFilters,
} from '../auditSlice';
import type { AuditLogEntryDto, AuditActionType } from '../../../types/api.types';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

function makeEntry(overrides: Partial<AuditLogEntryDto> = {}): AuditLogEntryDto {
  return {
    id: 'entry-001',
    userId: 'usr-001',
    userName: 'alice',
    actionType: 'Login',
    timestampUtc: '2025-07-10T08:00:00Z',
    ipAddress: '192.168.1.1',
    parameters: null,
    resultCount: null,
    s3ObjectKey: null,
    ...overrides,
  };
}

const entryLogin = makeEntry({ id: 'entry-001', actionType: 'Login' });
const entrySearch = makeEntry({
  id: 'entry-002',
  actionType: 'SearchTransactions',
  resultCount: 42,
  parameters: { method: 'GET' },
});
const entryView = makeEntry({
  id: 'entry-003',
  actionType: 'ViewTransaction',
  s3ObjectKey: 'logs/2025/07/10/req-001.gz',
});

const ALL_ENTRIES = [entryLogin, entrySearch, entryView];

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeInitialState(): AuditState {
  return auditReducer(undefined, { type: '@@INIT' });
}

function makeRootState(state: AuditState) {
  return { audit: state } as { audit: AuditState };
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('auditSlice', () => {
  // =========================================================================
  // Initial state
  // =========================================================================

  describe('initial state', () => {
    it('has an empty entries array', () => {
      expect(makeInitialState().entries).toEqual([]);
    });

    it('has totalCount of 0', () => {
      expect(makeInitialState().totalCount).toBe(0);
    });

    it('has currentPage of 1', () => {
      expect(makeInitialState().currentPage).toBe(1);
    });

    it('has an empty filters object', () => {
      expect(makeInitialState().filters).toEqual({});
    });

    it('has pageSize of 25', () => {
      expect(makeInitialState().pageSize).toBe(25);
    });
  });

  // =========================================================================
  // setAuditEntries
  // =========================================================================

  describe('setAuditEntries', () => {
    it('stores the entries array', () => {
      const state = auditReducer(
        makeInitialState(),
        setAuditEntries({ entries: ALL_ENTRIES, totalCount: 100, page: 1 }),
      );

      expect(state.entries).toEqual(ALL_ENTRIES);
    });

    it('stores totalCount from the payload', () => {
      const state = auditReducer(
        makeInitialState(),
        setAuditEntries({ entries: ALL_ENTRIES, totalCount: 200, page: 1 }),
      );

      expect(state.totalCount).toBe(200);
    });

    it('updates currentPage from the payload', () => {
      const state = auditReducer(
        makeInitialState(),
        setAuditEntries({ entries: ALL_ENTRIES, totalCount: 200, page: 5 }),
      );

      expect(state.currentPage).toBe(5);
    });

    it('replaces a previous entries page with new ones', () => {
      let state = auditReducer(
        makeInitialState(),
        setAuditEntries({ entries: ALL_ENTRIES, totalCount: 100, page: 1 }),
      );
      const page2 = [makeEntry({ id: 'entry-page2' })];
      state = auditReducer(
        state,
        setAuditEntries({ entries: page2, totalCount: 100, page: 2 }),
      );

      expect(state.entries).toHaveLength(1);
      expect(state.entries[0].id).toBe('entry-page2');
    });

    it('accepts an empty entries array (no results)', () => {
      const state = auditReducer(
        makeInitialState(),
        setAuditEntries({ entries: [], totalCount: 0, page: 1 }),
      );

      expect(state.entries).toEqual([]);
      expect(state.totalCount).toBe(0);
    });

    it('preserves all AuditLogEntryDto fields (parameters, s3ObjectKey, etc.)', () => {
      const state = auditReducer(
        makeInitialState(),
        setAuditEntries({ entries: [entrySearch, entryView], totalCount: 2, page: 1 }),
      );

      expect(state.entries[0].parameters).toEqual({ method: 'GET' });
      expect(state.entries[1].s3ObjectKey).toBe('logs/2025/07/10/req-001.gz');
    });
  });

  // =========================================================================
  // setAuditFilters
  // =========================================================================

  describe('setAuditFilters', () => {
    it('replaces the active filters', () => {
      const filters: AuditFilters = {
        fromDate: '2025-07-01T00:00:00Z',
        toDate: '2025-07-10T00:00:00Z',
        userId: 'usr-001',
        actionType: 'Login',
      };
      const state = auditReducer(makeInitialState(), setAuditFilters(filters));

      expect(state.filters).toEqual(filters);
    });

    it('resets currentPage to 1', () => {
      let state = auditReducer(makeInitialState(), setAuditCurrentPage(7));
      state = auditReducer(state, setAuditFilters({ userId: 'usr-002' }));

      expect(state.currentPage).toBe(1);
    });

    it('can set partial filters (only some fields)', () => {
      const state = auditReducer(
        makeInitialState(),
        setAuditFilters({ actionType: 'SearchTransactions' }),
      );

      expect(state.filters.actionType).toBe('SearchTransactions');
      expect(state.filters.userId).toBeUndefined();
    });

    it('can clear filters by setting an empty object', () => {
      let state = auditReducer(
        makeInitialState(),
        setAuditFilters({ userId: 'usr-001' }),
      );
      state = auditReducer(state, setAuditFilters({}));

      expect(state.filters).toEqual({});
    });

    it('accepts every AuditActionType value', () => {
      const actionTypes: AuditActionType[] = [
        'SearchTransactions',
        'ViewTransaction',
        'CreateCredentialProfile',
        'UpdateCredentialProfile',
        'DeleteCredentialProfile',
        'TestCredentialProfile',
        'ActivateCredentialProfile',
        'Login',
        'Logout',
        'LoginFailed',
        'AccountLocked',
      ];

      for (const actionType of actionTypes) {
        const state = auditReducer(makeInitialState(), setAuditFilters({ actionType }));
        expect(state.filters.actionType).toBe(actionType);
      }
    });
  });

  // =========================================================================
  // mergeAuditFilters
  // =========================================================================

  describe('mergeAuditFilters', () => {
    it('merges partial filters into existing filters', () => {
      let state = auditReducer(
        makeInitialState(),
        setAuditFilters({ userId: 'usr-001', actionType: 'Login' }),
      );
      state = auditReducer(
        state,
        mergeAuditFilters({ fromDate: '2025-07-01T00:00:00Z' }),
      );

      expect(state.filters.userId).toBe('usr-001');
      expect(state.filters.actionType).toBe('Login');
      expect(state.filters.fromDate).toBe('2025-07-01T00:00:00Z');
    });

    it('resets currentPage to 1 on any merge', () => {
      let state = auditReducer(makeInitialState(), setAuditCurrentPage(5));
      state = auditReducer(state, mergeAuditFilters({ userId: 'usr-abc' }));

      expect(state.currentPage).toBe(1);
    });

    it('overwrites an existing field with the new value', () => {
      let state = auditReducer(
        makeInitialState(),
        setAuditFilters({ userId: 'usr-001' }),
      );
      state = auditReducer(state, mergeAuditFilters({ userId: 'usr-999' }));

      expect(state.filters.userId).toBe('usr-999');
    });

    it('merging an empty partial leaves filters unchanged (but resets page)', () => {
      let state = auditReducer(
        makeInitialState(),
        setAuditFilters({ userId: 'usr-001' }),
      );
      state = auditReducer(state, setAuditCurrentPage(3));
      state = auditReducer(state, mergeAuditFilters({}));

      expect(state.filters.userId).toBe('usr-001');
      expect(state.currentPage).toBe(1);
    });
  });

  // =========================================================================
  // clearAuditFilters
  // =========================================================================

  describe('clearAuditFilters', () => {
    it('resets filters to an empty object', () => {
      let state = auditReducer(
        makeInitialState(),
        setAuditFilters({ userId: 'usr-001', actionType: 'Logout' }),
      );
      state = auditReducer(state, clearAuditFilters());

      expect(state.filters).toEqual({});
    });

    it('resets currentPage to 1', () => {
      let state = auditReducer(makeInitialState(), setAuditCurrentPage(6));
      state = auditReducer(state, clearAuditFilters());

      expect(state.currentPage).toBe(1);
    });

    it('does not affect existing entries', () => {
      let state = auditReducer(
        makeInitialState(),
        setAuditEntries({ entries: ALL_ENTRIES, totalCount: 3, page: 1 }),
      );
      state = auditReducer(
        state,
        setAuditFilters({ actionType: 'ViewTransaction' }),
      );
      state = auditReducer(state, clearAuditFilters());

      expect(state.entries).toHaveLength(3);
    });
  });

  // =========================================================================
  // setAuditCurrentPage
  // =========================================================================

  describe('setAuditCurrentPage', () => {
    it('updates currentPage', () => {
      const state = auditReducer(makeInitialState(), setAuditCurrentPage(10));

      expect(state.currentPage).toBe(10);
    });

    it('does NOT change filters', () => {
      let state = auditReducer(
        makeInitialState(),
        setAuditFilters({ userId: 'usr-001' }),
      );
      state = auditReducer(state, setAuditCurrentPage(3));

      expect(state.filters.userId).toBe('usr-001');
    });

    it('handles page 1 (minimum boundary)', () => {
      let state = auditReducer(makeInitialState(), setAuditCurrentPage(99));
      state = auditReducer(state, setAuditCurrentPage(1));

      expect(state.currentPage).toBe(1);
    });

    it('handles a very large page number', () => {
      const state = auditReducer(makeInitialState(), setAuditCurrentPage(100000));

      expect(state.currentPage).toBe(100000);
    });
  });

  // =========================================================================
  // setAuditPageSize
  // =========================================================================

  describe('setAuditPageSize', () => {
    it('updates pageSize', () => {
      const state = auditReducer(makeInitialState(), setAuditPageSize(50));

      expect(state.pageSize).toBe(50);
    });

    it('resets currentPage to 1 when page size changes', () => {
      let state = auditReducer(makeInitialState(), setAuditCurrentPage(4));
      state = auditReducer(state, setAuditPageSize(100));

      expect(state.currentPage).toBe(1);
    });

    it('handles page size of 1 (minimum boundary)', () => {
      const state = auditReducer(makeInitialState(), setAuditPageSize(1));

      expect(state.pageSize).toBe(1);
    });

    it('handles large page size', () => {
      const state = auditReducer(makeInitialState(), setAuditPageSize(200));

      expect(state.pageSize).toBe(200);
    });
  });

  // =========================================================================
  // clearAudit
  // =========================================================================

  describe('clearAudit', () => {
    it('resets entries to an empty array', () => {
      let state = auditReducer(
        makeInitialState(),
        setAuditEntries({ entries: ALL_ENTRIES, totalCount: 3, page: 1 }),
      );
      state = auditReducer(state, clearAudit());

      expect(state.entries).toEqual([]);
    });

    it('resets totalCount to 0', () => {
      let state = auditReducer(
        makeInitialState(),
        setAuditEntries({ entries: ALL_ENTRIES, totalCount: 99, page: 1 }),
      );
      state = auditReducer(state, clearAudit());

      expect(state.totalCount).toBe(0);
    });

    it('resets currentPage to 1', () => {
      let state = auditReducer(makeInitialState(), setAuditCurrentPage(9));
      state = auditReducer(state, clearAudit());

      expect(state.currentPage).toBe(1);
    });

    it('resets filters to an empty object', () => {
      let state = auditReducer(
        makeInitialState(),
        setAuditFilters({ userId: 'usr-001', actionType: 'Login' }),
      );
      state = auditReducer(state, clearAudit());

      expect(state.filters).toEqual({});
    });

    it('is idempotent on an already-empty state', () => {
      let state = auditReducer(makeInitialState(), clearAudit());
      state = auditReducer(state, clearAudit());

      expect(state.entries).toEqual([]);
      expect(state.filters).toEqual({});
    });
  });

  // =========================================================================
  // State immutability
  // =========================================================================

  describe('state immutability', () => {
    it('does not mutate the previous state on setAuditEntries', () => {
      const before = makeInitialState();
      const after = auditReducer(
        before,
        setAuditEntries({ entries: ALL_ENTRIES, totalCount: 3, page: 1 }),
      );

      expect(after).not.toBe(before);
    });

    it('does not mutate the previous state on mergeAuditFilters', () => {
      const before = auditReducer(
        makeInitialState(),
        setAuditFilters({ userId: 'usr-001' }),
      );
      const after = auditReducer(before, mergeAuditFilters({ actionType: 'Logout' }));

      expect(after).not.toBe(before);
    });
  });

  // =========================================================================
  // Selectors
  // =========================================================================

  describe('selectors', () => {
    let richState: AuditState;

    beforeEach(() => {
      let s = makeInitialState();
      s = auditReducer(
        s,
        setAuditEntries({ entries: ALL_ENTRIES, totalCount: 300, page: 4 }),
      );
      s = auditReducer(
        s,
        setAuditFilters({ userId: 'usr-001', actionType: 'Login' }),
      );
      s = auditReducer(s, setAuditPageSize(50));
      s = auditReducer(s, setAuditCurrentPage(4)); // reset after pageSize change
      richState = s;
    });

    it('selectAuditEntries returns the stored entries', () => {
      const root = makeRootState(richState);

      expect(selectAuditEntries(root as any)).toHaveLength(3);
    });

    it('selectAuditTotalCount returns totalCount', () => {
      const root = makeRootState(richState);

      expect(selectAuditTotalCount(root as any)).toBe(300);
    });

    it('selectAuditCurrentPage returns currentPage', () => {
      const root = makeRootState(richState);

      expect(selectAuditCurrentPage(root as any)).toBe(4);
    });

    it('selectAuditFilters returns the active filters', () => {
      const root = makeRootState(richState);
      const filters = selectAuditFilters(root as any);

      expect(filters.userId).toBe('usr-001');
      expect(filters.actionType).toBe('Login');
    });

    it('selectAuditPageSize returns the configured page size', () => {
      const root = makeRootState(richState);

      expect(selectAuditPageSize(root as any)).toBe(50);
    });

    describe('selectAuditQueryParams', () => {
      it('includes page and pageSize in the derived query params', () => {
        const root = makeRootState(richState);
        const params = selectAuditQueryParams(root as any);

        expect(params.page).toBe(4);
        expect(params.pageSize).toBe(50);
      });

      it('includes all active filter fields in the derived query params', () => {
        const root = makeRootState(richState);
        const params = selectAuditQueryParams(root as any);

        expect(params.userId).toBe('usr-001');
        expect(params.actionType).toBe('Login');
      });

      it('produces an empty filters block when filters are cleared', () => {
        let s = makeInitialState();
        s = auditReducer(s, clearAuditFilters());
        const root = makeRootState(s);
        const params = selectAuditQueryParams(root as any);

        expect(params.userId).toBeUndefined();
        expect(params.actionType).toBeUndefined();
        expect(params.fromDate).toBeUndefined();
        expect(params.page).toBe(1);
        expect(params.pageSize).toBe(25);
      });

      it('spreads date range filters when set', () => {
        let s = makeInitialState();
        s = auditReducer(
          s,
          setAuditFilters({
            fromDate: '2025-01-01T00:00:00Z',
            toDate: '2025-06-30T23:59:59Z',
          }),
        );
        const params = selectAuditQueryParams(makeRootState(s) as any);

        expect(params.fromDate).toBe('2025-01-01T00:00:00Z');
        expect(params.toDate).toBe('2025-06-30T23:59:59Z');
      });
    });
  });
});
