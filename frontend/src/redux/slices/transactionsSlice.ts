/**
 * transactionsSlice.ts
 * ====================
 * Manages client-side state for the Transactions search and detail feature.
 *
 * Responsibilities:
 *  - Hold the active search filter values so all components see the same
 *    query parameters (search form, pagination, URL sync).
 *  - Cache the current page of results and total record count returned
 *    by the search endpoint.
 *  - Track which transaction is currently open in the detail panel.
 *  - Surface a loading flag used by the search and detail views.
 *
 * The SearchFilter type is derived from SearchTransactionsParams (api.types.ts)
 * and represents the user-visible filter state in the Redux store.
 */

import { createSlice, PayloadAction } from '@reduxjs/toolkit';
import type { RootState } from '../store';
import type {
  SearchTransactionsParams,
  TransactionDetailDto,
  TransactionSummaryDto,
} from '../../types/api.types';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * SearchFilter mirrors SearchTransactionsParams but treats all fields as
 * optional so the store can be initialised with sensible defaults and
 * partial updates can be applied field-by-field.
 *
 * ASSUMPTION: SearchFilter is intentionally identical to SearchTransactionsParams
 * because every filter field maps directly to a query parameter. A type alias
 * is used so the name is semantically meaningful in the slice context.
 */
export type SearchFilter = SearchTransactionsParams;

export interface TransactionsState {
  /** Current search filters applied to the transaction list. */
  searchFilters: SearchFilter;
  /** Current page of transaction summaries from the last successful search. */
  results: TransactionSummaryDto[];
  /** Total number of records matching the current filter (for pagination). */
  totalCount: number;
  /** 1-based current page number. */
  currentPage: number;
  /**
   * The full detail of the currently selected transaction, or null if none
   * is selected or the detail is not yet loaded.
   */
  selectedTransaction: TransactionDetailDto | null;
  /** True while a search or detail fetch is in progress. */
  isLoading: boolean;
}

// ---------------------------------------------------------------------------
// Initial state
// ---------------------------------------------------------------------------

const initialState: TransactionsState = {
  searchFilters: {
    page: 1,
    pageSize: 20,
  },
  results: [],
  totalCount: 0,
  currentPage: 1,
  selectedTransaction: null,
  isLoading: false,
};

// ---------------------------------------------------------------------------
// Slice
// ---------------------------------------------------------------------------

const transactionsSlice = createSlice({
  name: 'transactions',
  initialState,

  reducers: {
    /**
     * Replaces the active search filters with the provided values.
     * Always resets to page 1 so a filter change doesn't leave the user
     * on an out-of-range page.
     */
    setSearchFilters(state, action: PayloadAction<SearchFilter>) {
      state.searchFilters = { ...action.payload, page: 1 };
      state.currentPage = 1;
    },

    /**
     * Merges partial filter updates into the existing filters.
     * Useful for single-field changes (e.g. changing only the status code).
     * Resets to page 1 on any filter change.
     */
    mergeSearchFilters(state, action: PayloadAction<Partial<SearchFilter>>) {
      state.searchFilters = {
        ...state.searchFilters,
        ...action.payload,
        page: 1,
      };
      state.currentPage = 1;
    },

    /**
     * Clears all filters back to the initial state.
     * Does NOT clear existing results — a new search must be triggered.
     */
    clearSearchFilters(state) {
      state.searchFilters = { ...initialState.searchFilters };
      state.currentPage = 1;
    },

    /**
     * Updates the current page in both the searchFilters (so the next query
     * uses the correct page) and currentPage (for display).
     */
    setCurrentPage(state, action: PayloadAction<number>) {
      state.currentPage = action.payload;
      state.searchFilters = {
        ...state.searchFilters,
        page: action.payload,
      };
    },

    /**
     * Stores the results of a completed search query.
     * Called by the component/thunk after the RTK Query hook resolves.
     */
    setSearchResults(
      state,
      action: PayloadAction<{
        results: TransactionSummaryDto[];
        totalCount: number;
        page: number;
      }>,
    ) {
      state.results = action.payload.results;
      state.totalCount = action.payload.totalCount;
      state.currentPage = action.payload.page;
    },

    /**
     * Stores the fully loaded detail for the selected transaction.
     * Pass null to deselect / close the detail panel.
     */
    setSelectedTransaction(
      state,
      action: PayloadAction<TransactionDetailDto | null>,
    ) {
      state.selectedTransaction = action.payload;
    },

    /** Controls the loading indicator for search and detail operations. */
    setIsLoading(state, action: PayloadAction<boolean>) {
      state.isLoading = action.payload;
    },

    /**
     * Clears results and resets the selected transaction.
     * Typically called on logout or when the user navigates away.
     */
    clearTransactions(state) {
      state.results = [];
      state.totalCount = 0;
      state.currentPage = 1;
      state.selectedTransaction = null;
      state.isLoading = false;
    },
  },
});

// ---------------------------------------------------------------------------
// Exports
// ---------------------------------------------------------------------------

export const {
  setSearchFilters,
  mergeSearchFilters,
  clearSearchFilters,
  setCurrentPage,
  setSearchResults,
  setSelectedTransaction,
  setIsLoading,
  clearTransactions,
} = transactionsSlice.actions;

// ---------------------------------------------------------------------------
// Selectors
// ---------------------------------------------------------------------------

export const selectSearchFilters = (state: RootState): SearchFilter =>
  state.transactions.searchFilters;

export const selectTransactionResults = (
  state: RootState,
): TransactionSummaryDto[] => state.transactions.results;

export const selectTransactionsTotalCount = (state: RootState): number =>
  state.transactions.totalCount;

export const selectTransactionsCurrentPage = (state: RootState): number =>
  state.transactions.currentPage;

export const selectSelectedTransaction = (
  state: RootState,
): TransactionDetailDto | null => state.transactions.selectedTransaction;

export const selectTransactionsIsLoading = (state: RootState): boolean =>
  state.transactions.isLoading;

export default transactionsSlice.reducer;
