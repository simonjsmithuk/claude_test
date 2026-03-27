/**
 * auditSlice.ts
 * =============
 * Manages client-side state for the Audit Log feature (Admin only).
 *
 * Responsibilities:
 *  - Cache the current page of audit log entries.
 *  - Track total record count and current page for pagination.
 *  - Store the active filter values (date range, userId, actionType) so
 *    all audit-log components share the same query parameters.
 *
 * The RTK Query `getAuditLogs` hook is the source of data; this slice holds
 * the UI-local state (filters, pagination) and a denormalised snapshot of
 * the last loaded page for components that don't directly subscribe to the
 * RTK Query result.
 */

import { createSlice, PayloadAction } from '@reduxjs/toolkit';
import type { RootState } from '../store';
import type {
  AuditActionType,
  AuditLogEntryDto,
  GetAuditLogsParams,
} from '../../types/api.types';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * The active filter state for the audit log view.
 * All fields are optional — omitting a field means "no filter on that field".
 * Mirrors GetAuditLogsParams but kept separate to make the Redux state shape
 * explicit and to allow UI-only fields to be added in future.
 */
export interface AuditFilters {
  /** ISO 8601 — filter entries on or after this date. */
  fromDate?: string;
  /** ISO 8601 — filter entries on or before this date. */
  toDate?: string;
  /** Filter by a specific user ID. */
  userId?: string;
  /** Filter by a specific audit action type. */
  actionType?: AuditActionType;
}

export interface AuditState {
  /** Current page of audit log entries from the last successful fetch. */
  entries: AuditLogEntryDto[];
  /** Total number of matching records (for pagination). */
  totalCount: number;
  /** 1-based current page number. */
  currentPage: number;
  /** Active filter values applied to the audit log list endpoint. */
  filters: AuditFilters;
  /**
   * Page size to use for the audit log query.
   * Kept here (not in filters) because it is less likely to be changed
   * by the user and deserves a clearly named dedicated field.
   */
  pageSize: number;
}

// ---------------------------------------------------------------------------
// Initial state
// ---------------------------------------------------------------------------

const initialState: AuditState = {
  entries: [],
  totalCount: 0,
  currentPage: 1,
  filters: {},
  pageSize: 25,
};

// ---------------------------------------------------------------------------
// Slice
// ---------------------------------------------------------------------------

const auditSlice = createSlice({
  name: 'audit',
  initialState,

  reducers: {
    /**
     * Stores the result of a successful audit log fetch.
     * Replaces the current page of entries in state.
     */
    setAuditEntries(
      state,
      action: PayloadAction<{
        entries: AuditLogEntryDto[];
        totalCount: number;
        page: number;
      }>,
    ) {
      state.entries = action.payload.entries;
      state.totalCount = action.payload.totalCount;
      state.currentPage = action.payload.page;
    },

    /**
     * Replaces the active audit filters.
     * Always resets to page 1 so a filter change doesn't land on an
     * out-of-range page.
     */
    setAuditFilters(state, action: PayloadAction<AuditFilters>) {
      state.filters = action.payload;
      state.currentPage = 1;
    },

    /**
     * Merges partial filter changes into the existing filter state.
     * Resets to page 1 on any change.
     */
    mergeAuditFilters(state, action: PayloadAction<Partial<AuditFilters>>) {
      state.filters = { ...state.filters, ...action.payload };
      state.currentPage = 1;
    },

    /** Clears all active filters back to empty. */
    clearAuditFilters(state) {
      state.filters = {};
      state.currentPage = 1;
    },

    /**
     * Updates the current page number.
     * Should be called when the user clicks a pagination control.
     */
    setAuditCurrentPage(state, action: PayloadAction<number>) {
      state.currentPage = action.payload;
    },

    /** Updates the page size for the audit log query. */
    setAuditPageSize(state, action: PayloadAction<number>) {
      state.pageSize = action.payload;
      state.currentPage = 1;
    },

    /**
     * Clears all audit state on logout so Admin data from one session
     * is not visible to subsequent sessions.
     */
    clearAudit(state) {
      state.entries = [];
      state.totalCount = 0;
      state.currentPage = 1;
      state.filters = {};
    },
  },
});

// ---------------------------------------------------------------------------
// Exports
// ---------------------------------------------------------------------------

export const {
  setAuditEntries,
  setAuditFilters,
  mergeAuditFilters,
  clearAuditFilters,
  setAuditCurrentPage,
  setAuditPageSize,
  clearAudit,
} = auditSlice.actions;

// ---------------------------------------------------------------------------
// Selectors
// ---------------------------------------------------------------------------

export const selectAuditEntries = (state: RootState): AuditLogEntryDto[] =>
  state.audit.entries;

export const selectAuditTotalCount = (state: RootState): number =>
  state.audit.totalCount;

export const selectAuditCurrentPage = (state: RootState): number =>
  state.audit.currentPage;

export const selectAuditFilters = (state: RootState): AuditFilters =>
  state.audit.filters;

export const selectAuditPageSize = (state: RootState): number =>
  state.audit.pageSize;

/**
 * Derives the complete GetAuditLogsParams object from slice state,
 * ready to pass directly to the RTK Query hook.
 */
export const selectAuditQueryParams = (state: RootState): GetAuditLogsParams => ({
  ...state.audit.filters,
  page: state.audit.currentPage,
  pageSize: state.audit.pageSize,
});

export default auditSlice.reducer;
