/**
 * transactionsSlice.test.ts
 * ==========================
 * Unit tests for the transactions Redux slice.
 *
 * Coverage targets:
 *  - Initial state shape and default values
 *  - setSearchFilters: full replacement, always resets to page 1
 *  - mergeSearchFilters: partial merge, always resets to page 1
 *  - clearSearchFilters: restores defaults without clearing results
 *  - setCurrentPage: updates both currentPage and searchFilters.page
 *  - setSearchResults: stores results, totalCount, and page
 *  - setSelectedTransaction: stores and nullifies the selected detail
 *  - setIsLoading: toggles loading flag
 *  - clearTransactions: full reset (results, page, selected, loading)
 *  - All selectors
 *  - Edge cases: empty results, page boundary values, null selected transaction
 */

import transactionsReducer, {
  setSearchFilters,
  mergeSearchFilters,
  clearSearchFilters,
  setCurrentPage,
  setSearchResults,
  setSelectedTransaction,
  setIsLoading,
  clearTransactions,
  selectSearchFilters,
  selectTransactionResults,
  selectTransactionsTotalCount,
  selectTransactionsCurrentPage,
  selectSelectedTransaction,
  selectTransactionsIsLoading,
  type TransactionsState,
  type SearchFilter,
} from '../transactionsSlice';
import type {
  TransactionSummaryDto,
  TransactionDetailDto,
} from '../../../types/api.types';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

function makeSummary(overrides: Partial<TransactionSummaryDto> = {}): TransactionSummaryDto {
  return {
    s3Key: 'logs/2025/07/10/req-001.gz',
    method: 'GET',
    statusCode: 200,
    urlPath: '/api/health',
    timestampUtc: '2025-07-10T09:00:00Z',
    fileSizeBytes: 1024,
    ...overrides,
  };
}

function makeDetail(overrides: Partial<TransactionDetailDto> = {}): TransactionDetailDto {
  return {
    metadata: {
      s3Key: 'logs/2025/07/10/req-001.gz',
      compressedSizeBytes: 512,
      decompressedSizeBytes: 1024,
      timestampUtc: '2025-07-10T09:00:00Z',
      s3LastModified: '2025-07-10T09:01:00Z',
    },
    request: {
      method: 'GET',
      urlPath: '/api/health',
      httpVersion: 'HTTP/1.1',
      headers: { 'Content-Type': 'application/json' },
      body: null,
      bodyContentType: null,
      isBodyTruncated: false,
    },
    response: {
      httpVersion: 'HTTP/1.1',
      statusCode: 200,
      statusText: 'OK',
      headers: { 'Content-Type': 'application/json' },
      body: '{"status":"ok"}',
      bodyContentType: 'json',
      isBodyTruncated: false,
    },
    ...overrides,
  };
}

const PAGE_1_RESULTS = [
  makeSummary({ s3Key: 'key-1', statusCode: 200 }),
  makeSummary({ s3Key: 'key-2', statusCode: 404 }),
];

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeInitialState(): TransactionsState {
  return transactionsReducer(undefined, { type: '@@INIT' });
}

function makeRootState(state: TransactionsState) {
  return { transactions: state } as { transactions: TransactionsState };
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('transactionsSlice', () => {
  // =========================================================================
  // Initial state
  // =========================================================================

  describe('initial state', () => {
    it('initialises searchFilters with page=1 and pageSize=20', () => {
      const { searchFilters } = makeInitialState();

      expect(searchFilters.page).toBe(1);
      expect(searchFilters.pageSize).toBe(20);
    });

    it('initialises results as an empty array', () => {
      expect(makeInitialState().results).toEqual([]);
    });

    it('initialises totalCount as 0', () => {
      expect(makeInitialState().totalCount).toBe(0);
    });

    it('initialises currentPage as 1', () => {
      expect(makeInitialState().currentPage).toBe(1);
    });

    it('initialises selectedTransaction as null', () => {
      expect(makeInitialState().selectedTransaction).toBeNull();
    });

    it('initialises isLoading as false', () => {
      expect(makeInitialState().isLoading).toBe(false);
    });
  });

  // =========================================================================
  // setSearchFilters
  // =========================================================================

  describe('setSearchFilters', () => {
    it('replaces all search filters with the provided value', () => {
      const newFilters: SearchFilter = {
        method: 'POST',
        statusCode: '201',
        page: 3,
        pageSize: 50,
      };
      const state = transactionsReducer(makeInitialState(), setSearchFilters(newFilters));

      expect(state.searchFilters.method).toBe('POST');
      expect(state.searchFilters.statusCode).toBe('201');
      expect(state.searchFilters.pageSize).toBe(50);
    });

    it('always resets page to 1 regardless of payload page value', () => {
      const state = transactionsReducer(
        makeInitialState(),
        setSearchFilters({ method: 'GET', page: 5, pageSize: 20 }),
      );

      expect(state.searchFilters.page).toBe(1);
    });

    it('resets currentPage to 1', () => {
      let state = transactionsReducer(makeInitialState(), setCurrentPage(4));
      state = transactionsReducer(
        state,
        setSearchFilters({ statusCode: '500', page: 4, pageSize: 20 }),
      );

      expect(state.currentPage).toBe(1);
    });

    it('can set an empty filter object (only page/pageSize injected)', () => {
      const state = transactionsReducer(makeInitialState(), setSearchFilters({}));

      expect(state.searchFilters).toEqual({ page: 1 });
    });

    it('accepts all optional filter fields', () => {
      const rich: SearchFilter = {
        fromDate: '2025-01-01T00:00:00Z',
        toDate: '2025-07-10T00:00:00Z',
        statusCode: '200',
        statusClass: '2xx',
        method: 'DELETE',
        urlPrefix: '/api/',
        page: 1,
        pageSize: 10,
        profileId: 'prof-123',
      };
      const state = transactionsReducer(makeInitialState(), setSearchFilters(rich));

      expect(state.searchFilters.fromDate).toBe('2025-01-01T00:00:00Z');
      expect(state.searchFilters.profileId).toBe('prof-123');
    });
  });

  // =========================================================================
  // mergeSearchFilters
  // =========================================================================

  describe('mergeSearchFilters', () => {
    it('merges partial filters into existing filters', () => {
      let state = transactionsReducer(
        makeInitialState(),
        setSearchFilters({ method: 'GET', pageSize: 20, page: 1 }),
      );
      state = transactionsReducer(
        state,
        mergeSearchFilters({ statusCode: '404' }),
      );

      expect(state.searchFilters.method).toBe('GET');
      expect(state.searchFilters.statusCode).toBe('404');
    });

    it('resets page to 1 after a merge', () => {
      let state = transactionsReducer(makeInitialState(), setCurrentPage(3));
      state = transactionsReducer(state, mergeSearchFilters({ method: 'POST' }));

      expect(state.searchFilters.page).toBe(1);
      expect(state.currentPage).toBe(1);
    });

    it('overwrites a previously set field', () => {
      let state = transactionsReducer(
        makeInitialState(),
        setSearchFilters({ method: 'GET', page: 1, pageSize: 20 }),
      );
      state = transactionsReducer(state, mergeSearchFilters({ method: 'PUT' }));

      expect(state.searchFilters.method).toBe('PUT');
    });

    it('can merge an empty partial without changing other fields (except page reset)', () => {
      let state = transactionsReducer(
        makeInitialState(),
        setSearchFilters({ method: 'GET', page: 2, pageSize: 20 }),
      );
      state = transactionsReducer(state, setCurrentPage(2));
      state = transactionsReducer(state, mergeSearchFilters({}));

      expect(state.searchFilters.method).toBe('GET');
      expect(state.searchFilters.page).toBe(1); // always reset
    });
  });

  // =========================================================================
  // clearSearchFilters
  // =========================================================================

  describe('clearSearchFilters', () => {
    it('resets searchFilters to the initial default', () => {
      let state = transactionsReducer(
        makeInitialState(),
        setSearchFilters({ method: 'POST', statusCode: '500', page: 3, pageSize: 50 }),
      );
      state = transactionsReducer(state, clearSearchFilters());

      expect(state.searchFilters).toEqual({ page: 1, pageSize: 20 });
    });

    it('resets currentPage to 1', () => {
      let state = transactionsReducer(makeInitialState(), setCurrentPage(7));
      state = transactionsReducer(state, clearSearchFilters());

      expect(state.currentPage).toBe(1);
    });

    it('does NOT clear existing results', () => {
      let state = transactionsReducer(
        makeInitialState(),
        setSearchResults({ results: PAGE_1_RESULTS, totalCount: 2, page: 1 }),
      );
      state = transactionsReducer(state, clearSearchFilters());

      expect(state.results).toHaveLength(2);
      expect(state.totalCount).toBe(2);
    });
  });

  // =========================================================================
  // setCurrentPage
  // =========================================================================

  describe('setCurrentPage', () => {
    it('updates currentPage', () => {
      const state = transactionsReducer(makeInitialState(), setCurrentPage(5));

      expect(state.currentPage).toBe(5);
    });

    it('mirrors the page into searchFilters.page', () => {
      const state = transactionsReducer(makeInitialState(), setCurrentPage(5));

      expect(state.searchFilters.page).toBe(5);
    });

    it('handles page 1 (boundary value)', () => {
      let state = transactionsReducer(makeInitialState(), setCurrentPage(10));
      state = transactionsReducer(state, setCurrentPage(1));

      expect(state.currentPage).toBe(1);
      expect(state.searchFilters.page).toBe(1);
    });

    it('handles large page numbers', () => {
      const state = transactionsReducer(makeInitialState(), setCurrentPage(9999));

      expect(state.currentPage).toBe(9999);
    });
  });

  // =========================================================================
  // setSearchResults
  // =========================================================================

  describe('setSearchResults', () => {
    it('stores the results array', () => {
      const state = transactionsReducer(
        makeInitialState(),
        setSearchResults({ results: PAGE_1_RESULTS, totalCount: 100, page: 1 }),
      );

      expect(state.results).toEqual(PAGE_1_RESULTS);
    });

    it('stores totalCount', () => {
      const state = transactionsReducer(
        makeInitialState(),
        setSearchResults({ results: PAGE_1_RESULTS, totalCount: 100, page: 1 }),
      );

      expect(state.totalCount).toBe(100);
    });

    it('updates currentPage from the payload', () => {
      const state = transactionsReducer(
        makeInitialState(),
        setSearchResults({ results: PAGE_1_RESULTS, totalCount: 100, page: 4 }),
      );

      expect(state.currentPage).toBe(4);
    });

    it('accepts an empty results array (clears previous results)', () => {
      let state = transactionsReducer(
        makeInitialState(),
        setSearchResults({ results: PAGE_1_RESULTS, totalCount: 2, page: 1 }),
      );
      state = transactionsReducer(
        state,
        setSearchResults({ results: [], totalCount: 0, page: 1 }),
      );

      expect(state.results).toEqual([]);
      expect(state.totalCount).toBe(0);
    });

    it('replaces previous results entirely', () => {
      const page2 = [makeSummary({ s3Key: 'page2-key-1' })];
      let state = transactionsReducer(
        makeInitialState(),
        setSearchResults({ results: PAGE_1_RESULTS, totalCount: 3, page: 1 }),
      );
      state = transactionsReducer(
        state,
        setSearchResults({ results: page2, totalCount: 3, page: 2 }),
      );

      expect(state.results).toHaveLength(1);
      expect(state.results[0].s3Key).toBe('page2-key-1');
    });
  });

  // =========================================================================
  // setSelectedTransaction
  // =========================================================================

  describe('setSelectedTransaction', () => {
    it('stores the transaction detail', () => {
      const detail = makeDetail();
      const state = transactionsReducer(
        makeInitialState(),
        setSelectedTransaction(detail),
      );

      expect(state.selectedTransaction).toEqual(detail);
    });

    it('clears the selected transaction when null is dispatched', () => {
      let state = transactionsReducer(
        makeInitialState(),
        setSelectedTransaction(makeDetail()),
      );
      state = transactionsReducer(state, setSelectedTransaction(null));

      expect(state.selectedTransaction).toBeNull();
    });

    it('replaces a previously selected transaction', () => {
      const first = makeDetail();
      const second = makeDetail({
        metadata: { ...makeDetail().metadata, s3Key: 'second-key' },
      });

      let state = transactionsReducer(makeInitialState(), setSelectedTransaction(first));
      state = transactionsReducer(state, setSelectedTransaction(second));

      expect(state.selectedTransaction?.metadata.s3Key).toBe('second-key');
    });
  });

  // =========================================================================
  // setIsLoading
  // =========================================================================

  describe('setIsLoading', () => {
    it('sets isLoading to true', () => {
      const state = transactionsReducer(makeInitialState(), setIsLoading(true));

      expect(state.isLoading).toBe(true);
    });

    it('sets isLoading back to false', () => {
      let state = transactionsReducer(makeInitialState(), setIsLoading(true));
      state = transactionsReducer(state, setIsLoading(false));

      expect(state.isLoading).toBe(false);
    });
  });

  // =========================================================================
  // clearTransactions
  // =========================================================================

  describe('clearTransactions', () => {
    it('clears results', () => {
      let state = transactionsReducer(
        makeInitialState(),
        setSearchResults({ results: PAGE_1_RESULTS, totalCount: 2, page: 1 }),
      );
      state = transactionsReducer(state, clearTransactions());

      expect(state.results).toEqual([]);
    });

    it('resets totalCount to 0', () => {
      let state = transactionsReducer(
        makeInitialState(),
        setSearchResults({ results: PAGE_1_RESULTS, totalCount: 99, page: 1 }),
      );
      state = transactionsReducer(state, clearTransactions());

      expect(state.totalCount).toBe(0);
    });

    it('resets currentPage to 1', () => {
      let state = transactionsReducer(makeInitialState(), setCurrentPage(8));
      state = transactionsReducer(state, clearTransactions());

      expect(state.currentPage).toBe(1);
    });

    it('clears selectedTransaction', () => {
      let state = transactionsReducer(
        makeInitialState(),
        setSelectedTransaction(makeDetail()),
      );
      state = transactionsReducer(state, clearTransactions());

      expect(state.selectedTransaction).toBeNull();
    });

    it('sets isLoading to false', () => {
      let state = transactionsReducer(makeInitialState(), setIsLoading(true));
      state = transactionsReducer(state, clearTransactions());

      expect(state.isLoading).toBe(false);
    });
  });

  // =========================================================================
  // Selectors
  // =========================================================================

  describe('selectors', () => {
    let richState: TransactionsState;

    beforeEach(() => {
      const detail = makeDetail();
      let s = makeInitialState();
      s = transactionsReducer(
        s,
        setSearchResults({ results: PAGE_1_RESULTS, totalCount: 42, page: 3 }),
      );
      s = transactionsReducer(s, setSelectedTransaction(detail));
      s = transactionsReducer(s, setIsLoading(true));
      s = transactionsReducer(
        s,
        setSearchFilters({ method: 'GET', page: 1, pageSize: 20 }),
      );
      richState = s;
    });

    it('selectSearchFilters returns the current filters', () => {
      const root = makeRootState(richState);
      const filters = selectSearchFilters(root as any);

      expect(filters.method).toBe('GET');
    });

    it('selectTransactionResults returns the results array', () => {
      const root = makeRootState(richState);

      expect(selectTransactionResults(root as any)).toHaveLength(2);
    });

    it('selectTransactionsTotalCount returns totalCount', () => {
      // totalCount was set before setSearchFilters reset currentPage
      const root = makeRootState(richState);

      expect(selectTransactionsTotalCount(root as any)).toBe(42);
    });

    it('selectTransactionsCurrentPage returns currentPage', () => {
      let s = makeInitialState();
      s = transactionsReducer(
        s,
        setSearchResults({ results: PAGE_1_RESULTS, totalCount: 42, page: 5 }),
      );
      const root = makeRootState(s);

      expect(selectTransactionsCurrentPage(root as any)).toBe(5);
    });

    it('selectSelectedTransaction returns the selected detail', () => {
      const detail = makeDetail();
      let s = makeInitialState();
      s = transactionsReducer(s, setSelectedTransaction(detail));
      const root = makeRootState(s);

      expect(selectSelectedTransaction(root as any)).toEqual(detail);
    });

    it('selectSelectedTransaction returns null from initial state', () => {
      const root = makeRootState(makeInitialState());

      expect(selectSelectedTransaction(root as any)).toBeNull();
    });

    it('selectTransactionsIsLoading returns the loading flag', () => {
      let s = makeInitialState();
      s = transactionsReducer(s, setIsLoading(true));
      const root = makeRootState(s);

      expect(selectTransactionsIsLoading(root as any)).toBe(true);
    });
  });
});
