/**
 * preferencesSlice.ts
 * ===================
 * Manages client-side state for the authenticated user's preferences.
 *
 * Responsibilities:
 *  - Hold the current UserPreferenceDto fetched from GET /users/me/preferences.
 *  - Provide a `setPreferences` action so components can apply the result of
 *    the RTK Query hook into Redux for cross-slice consumption (e.g. the
 *    default page size drives transaction search pagination).
 *
 * The preferences are always loaded fresh from the server after login and are
 * NOT persisted locally — the server is the source of truth.
 */

import { createSlice, PayloadAction } from '@reduxjs/toolkit';
import type { RootState } from '../store';
import type { UserPreferenceDto } from '../../types/api.types';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface PreferencesState {
  /**
   * The user's stored preferences. Null until the first successful fetch.
   * Components should treat null as "not yet loaded" and show a loading state
   * or fall back to application defaults.
   */
  preferences: UserPreferenceDto | null;
  /**
   * True while the preferences are being fetched or saved.
   * Managed separately from the global uiSlice loading flag so components
   * can show localised indicators (e.g. a spinner on the Save button).
   */
  isLoading: boolean;
}

// ---------------------------------------------------------------------------
// Application defaults — used when preferences haven't loaded yet
// ---------------------------------------------------------------------------

/**
 * Fallback values that mirror the server-side defaults (System Design §5.5).
 * Components may import these when preferences is null.
 */
export const PREFERENCE_DEFAULTS: UserPreferenceDto = {
  defaultPageSize: 20,
  defaultDateRangeDays: 7,
  preferredProfileId: null,
};

// ---------------------------------------------------------------------------
// Initial state
// ---------------------------------------------------------------------------

const initialState: PreferencesState = {
  preferences: null,
  isLoading: false,
};

// ---------------------------------------------------------------------------
// Slice
// ---------------------------------------------------------------------------

const preferencesSlice = createSlice({
  name: 'preferences',
  initialState,

  reducers: {
    /**
     * Stores the user's preferences in Redux state.
     * Called after a successful GET or PUT /users/me/preferences response.
     */
    setPreferences(state, action: PayloadAction<UserPreferenceDto>) {
      state.preferences = action.payload;
    },

    /**
     * Applies a partial update optimistically before the server confirms.
     * If the server call fails, callers should dispatch setPreferences with
     * the original value to roll back.
     */
    patchPreferences(state, action: PayloadAction<Partial<UserPreferenceDto>>) {
      if (state.preferences !== null) {
        state.preferences = { ...state.preferences, ...action.payload };
      }
    },

    /** Controls a localised loading indicator for preferences operations. */
    setPreferencesLoading(state, action: PayloadAction<boolean>) {
      state.isLoading = action.payload;
    },

    /**
     * Clears preferences state on logout so the next user's data
     * is not leaked to a subsequent session.
     */
    clearPreferences(state) {
      state.preferences = null;
      state.isLoading = false;
    },
  },
});

// ---------------------------------------------------------------------------
// Exports
// ---------------------------------------------------------------------------

export const {
  setPreferences,
  patchPreferences,
  setPreferencesLoading,
  clearPreferences,
} = preferencesSlice.actions;

// ---------------------------------------------------------------------------
// Selectors
// ---------------------------------------------------------------------------

export const selectPreferences = (state: RootState): UserPreferenceDto | null =>
  state.preferences.preferences;

/**
 * Returns the preferences with application defaults applied for any null
 * field. Safe to call before the preferences have loaded.
 */
export const selectPreferencesWithDefaults = (
  state: RootState,
): UserPreferenceDto =>
  state.preferences.preferences ?? PREFERENCE_DEFAULTS;

export const selectPreferencesIsLoading = (state: RootState): boolean =>
  state.preferences.isLoading;

export const selectDefaultPageSize = (state: RootState): number =>
  state.preferences.preferences?.defaultPageSize ??
  PREFERENCE_DEFAULTS.defaultPageSize;

export const selectDefaultDateRangeDays = (state: RootState): number =>
  state.preferences.preferences?.defaultDateRangeDays ??
  PREFERENCE_DEFAULTS.defaultDateRangeDays;

export const selectPreferredProfileId = (state: RootState): string | null =>
  state.preferences.preferences?.preferredProfileId ?? null;

export default preferencesSlice.reducer;
