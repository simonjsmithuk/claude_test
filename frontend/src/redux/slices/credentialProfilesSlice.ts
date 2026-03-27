/**
 * credentialProfilesSlice.ts
 * ==========================
 * Manages client-side state for the Credential Profiles feature.
 *
 * Responsibilities:
 *  - Cache the list of profiles fetched via RTK Query (lightweight mirror
 *    for components that need the list without subscribing to the query).
 *  - Track which profile is currently active (activeProfileId).
 *  - Store per-profile S3 connection test status so the UI can show
 *    in-progress / success / failure indicators without another network call.
 *
 * NOTE: The authoritative data lives in the RTK Query cache (dataViewerApi).
 *       This slice is the source of truth only for UI-local state (test
 *       statuses, the active-profile selection) that doesn't have a natural
 *       home in the server cache.
 */

import { createSlice, PayloadAction } from '@reduxjs/toolkit';
import type { RootState } from '../store';
import type { CredentialProfileDto, TestConnectionResult } from '../../types/api.types';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Possible states for an in-progress or completed connection test. */
export type TestConnectionStatus =
  | 'idle'
  | 'pending'
  | 'success'
  | 'failure';

/** Richer test result stored per profileId for display in the UI. */
export interface ProfileTestState {
  status: TestConnectionStatus;
  /** Last result returned from the API, if any. */
  result: TestConnectionResult | null;
  /** ISO 8601 UTC — when the last test was run. */
  lastTestedAt: string | null;
}

export interface CredentialProfilesState {
  /** Flat list of profiles — synced from the RTK Query result. */
  profiles: CredentialProfileDto[];
  /** The id of the profile currently marked as active, if any. */
  activeProfileId: string | null;
  /**
   * Per-profile test-connection state, keyed by profileId.
   * Missing entries imply 'idle'.
   */
  testConnectionStatus: Record<string, ProfileTestState>;
}

// ---------------------------------------------------------------------------
// Initial state
// ---------------------------------------------------------------------------

const initialState: CredentialProfilesState = {
  profiles: [],
  activeProfileId: null,
  testConnectionStatus: {},
};

// ---------------------------------------------------------------------------
// Helper — default test state sentinel
// ---------------------------------------------------------------------------

const defaultTestState: ProfileTestState = {
  status: 'idle',
  result: null,
  lastTestedAt: null,
};

// ---------------------------------------------------------------------------
// Slice
// ---------------------------------------------------------------------------

const credentialProfilesSlice = createSlice({
  name: 'credentialProfiles',
  initialState,

  reducers: {
    /**
     * Replaces the entire profiles list (called when the RTK Query list
     * endpoint resolves). Also syncs activeProfileId from the data.
     */
    setProfiles(state, action: PayloadAction<CredentialProfileDto[]>) {
      state.profiles = action.payload;
      // Derive activeProfileId from the data so the two stay in sync.
      const active = action.payload.find((p) => p.isActive && !p.isDeleted);
      state.activeProfileId = active?.id ?? null;
    },

    /**
     * Upserts a single profile into the list (after create or update).
     * If a profile with the same id already exists, it is replaced.
     */
    upsertProfile(state, action: PayloadAction<CredentialProfileDto>) {
      const idx = state.profiles.findIndex((p) => p.id === action.payload.id);
      if (idx !== -1) {
        state.profiles[idx] = action.payload;
      } else {
        state.profiles.push(action.payload);
      }
      // Re-derive active id in case isActive changed.
      if (action.payload.isActive) {
        state.activeProfileId = action.payload.id;
      }
    },

    /**
     * Removes a profile from the local list (soft-delete confirmation).
     * The server performs a soft-delete; we remove from the UI list entirely.
     */
    removeProfile(state, action: PayloadAction<string>) {
      state.profiles = state.profiles.filter((p) => p.id !== action.payload);
      if (state.activeProfileId === action.payload) {
        state.activeProfileId = null;
      }
      // Clean up test state for the deleted profile.
      delete state.testConnectionStatus[action.payload];
    },

    /** Sets the active profile id directly (after an activate API call). */
    setActiveProfileId(state, action: PayloadAction<string>) {
      state.activeProfileId = action.payload;
      // Mirror isActive flag across the cached profiles list.
      state.profiles = state.profiles.map((p) => ({
        ...p,
        isActive: p.id === action.payload,
      }));
    },

    /** Marks a profile's test as in-progress. */
    setTestConnectionPending(state, action: PayloadAction<string>) {
      state.testConnectionStatus[action.payload] = {
        status: 'pending',
        result: null,
        lastTestedAt: null,
      };
    },

    /** Stores the result of a completed connection test (success or failure). */
    setTestConnectionResult(
      state,
      action: PayloadAction<{ profileId: string; result: TestConnectionResult }>,
    ) {
      const { profileId, result } = action.payload;
      state.testConnectionStatus[profileId] = {
        status: result.success ? 'success' : 'failure',
        result,
        lastTestedAt: result.testedAt,
      };
    },

    /** Resets the test status for a profile back to idle. */
    resetTestConnectionStatus(state, action: PayloadAction<string>) {
      state.testConnectionStatus[action.payload] = { ...defaultTestState };
    },

    /** Clears all local profile state (e.g. on logout). */
    clearProfiles(state) {
      state.profiles = [];
      state.activeProfileId = null;
      state.testConnectionStatus = {};
    },
  },
});

// ---------------------------------------------------------------------------
// Exports
// ---------------------------------------------------------------------------

export const {
  setProfiles,
  upsertProfile,
  removeProfile,
  setActiveProfileId,
  setTestConnectionPending,
  setTestConnectionResult,
  resetTestConnectionStatus,
  clearProfiles,
} = credentialProfilesSlice.actions;

// ---------------------------------------------------------------------------
// Selectors
// ---------------------------------------------------------------------------

export const selectAllProfiles = (state: RootState): CredentialProfileDto[] =>
  state.credentialProfiles.profiles;

export const selectActiveProfileId = (state: RootState): string | null =>
  state.credentialProfiles.activeProfileId;

export const selectActiveProfile = (
  state: RootState,
): CredentialProfileDto | undefined =>
  state.credentialProfiles.profiles.find(
    (p) => p.id === state.credentialProfiles.activeProfileId,
  );

/** Returns the test state for a specific profile, defaulting to 'idle'. */
export const selectTestConnectionStatus =
  (profileId: string) =>
  (state: RootState): ProfileTestState =>
    state.credentialProfiles.testConnectionStatus[profileId] ?? defaultTestState;

export default credentialProfilesSlice.reducer;
