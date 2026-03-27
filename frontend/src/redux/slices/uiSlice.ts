/**
 * uiSlice.ts
 * ==========
 * Manages global UI state shared across multiple features:
 *  - A global loading overlay flag (isLoading).
 *  - A transient error message shown in a toast / alert banner.
 *  - A transient success message shown in a toast / alert banner.
 *
 * This slice intentionally does NOT hold feature-specific loading states
 * (e.g. the credential test indicator) — those belong in their own slices.
 * Use this slice for application-level notifications triggered by actions
 * in thunks or event handlers where no better location exists.
 *
 * Pattern for displaying a message then auto-clearing it:
 *   dispatch(setError('Something went wrong.'));
 *   // In the component: useEffect(() => { return () => dispatch(clearMessages()); }, []);
 */

import { createSlice, PayloadAction } from '@reduxjs/toolkit';
import type { RootState } from '../store';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface UiState {
  /**
   * When true, a full-screen loading overlay should be displayed.
   * Components that have their own localised loading state should NOT use
   * this flag — use the slice-local isLoading fields instead.
   */
  isLoading: boolean;
  /**
   * A human-readable error message to display in a global notification.
   * Null when no error is active.
   */
  errorMessage: string | null;
  /**
   * A human-readable success message to display in a global notification.
   * Null when no success notification is active.
   */
  successMessage: string | null;
}

// ---------------------------------------------------------------------------
// Initial state
// ---------------------------------------------------------------------------

const initialState: UiState = {
  isLoading: false,
  errorMessage: null,
  successMessage: null,
};

// ---------------------------------------------------------------------------
// Slice
// ---------------------------------------------------------------------------

const uiSlice = createSlice({
  name: 'ui',
  initialState,

  reducers: {
    /**
     * Controls the global loading overlay.
     * Pass true to show the spinner, false to hide it.
     */
    setLoading(state, action: PayloadAction<boolean>) {
      state.isLoading = action.payload;
    },

    /**
     * Sets a global error message.
     * Any existing success message is cleared automatically so the two
     * notifications do not appear simultaneously.
     */
    setError(state, action: PayloadAction<string>) {
      state.errorMessage = action.payload;
      state.successMessage = null;
    },

    /**
     * Sets a global success message.
     * Any existing error message is cleared automatically.
     */
    setSuccess(state, action: PayloadAction<string>) {
      state.successMessage = action.payload;
      state.errorMessage = null;
    },

    /**
     * Clears both the error and success messages.
     * Call this when the user dismisses a notification or when navigating away.
     */
    clearMessages(state) {
      state.errorMessage = null;
      state.successMessage = null;
    },

    /**
     * Resets the entire UI state back to initial.
     * Useful on logout to ensure no stale notifications remain.
     */
    resetUi() {
      return initialState;
    },
  },
});

// ---------------------------------------------------------------------------
// Exports
// ---------------------------------------------------------------------------

export const {
  setLoading,
  setError,
  setSuccess,
  clearMessages,
  resetUi,
} = uiSlice.actions;

// ---------------------------------------------------------------------------
// Selectors
// ---------------------------------------------------------------------------

export const selectIsLoading = (state: RootState): boolean =>
  state.ui.isLoading;

export const selectErrorMessage = (state: RootState): string | null =>
  state.ui.errorMessage;

export const selectSuccessMessage = (state: RootState): string | null =>
  state.ui.successMessage;

/**
 * Convenience selector — true when either an error or success message is
 * currently active. Useful for deciding whether to render a notification area.
 */
export const selectHasNotification = (state: RootState): boolean =>
  state.ui.errorMessage !== null || state.ui.successMessage !== null;

export default uiSlice.reducer;
