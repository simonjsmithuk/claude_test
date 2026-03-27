/**
 * authSlice.ts
 * ============
 * Manages authentication state: access token, refresh token, and basic user
 * identity (id, username, role).
 *
 * Token persistence strategy:
 *  - accessToken  → sessionStorage (cleared on tab close; never localStorage)
 *  - refreshToken → sessionStorage (mirrors accessToken lifetime in browser)
 *
 * The actual refresh-token exchange is handled by the 401 interceptor in
 * apiClient.ts, not by this slice.
 */

import { createSlice, PayloadAction } from '@reduxjs/toolkit';
import type { RootState } from '../store';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export type UserRole = 'Admin' | 'Viewer';

export interface AuthUser {
  id: string;
  username: string;
  role: UserRole;
}

export interface AuthState {
  accessToken: string | null;
  refreshToken: string | null;
  /** Decoded user identity from the JWT claims. */
  user: AuthUser | null;
  isAuthenticated: boolean;
  /** ISO 8601 UTC expiry of the access token. */
  expiresAt: string | null;
}

export interface SetCredentialsPayload {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  user: AuthUser;
}

// ---------------------------------------------------------------------------
// Session-storage helpers
// ---------------------------------------------------------------------------

const SESSION_KEY_ACCESS = 'dv_access_token';
const SESSION_KEY_REFRESH = 'dv_refresh_token';

function persistTokens(access: string, refresh: string): void {
  try {
    sessionStorage.setItem(SESSION_KEY_ACCESS, access);
    sessionStorage.setItem(SESSION_KEY_REFRESH, refresh);
  } catch {
    // Storage not available (e.g. private browsing, iframe restrictions).
  }
}

function clearPersistedTokens(): void {
  try {
    sessionStorage.removeItem(SESSION_KEY_ACCESS);
    sessionStorage.removeItem(SESSION_KEY_REFRESH);
  } catch {
    // Ignore.
  }
}

function loadPersistedTokens(): Pick<AuthState, 'accessToken' | 'refreshToken'> {
  try {
    return {
      accessToken: sessionStorage.getItem(SESSION_KEY_ACCESS),
      refreshToken: sessionStorage.getItem(SESSION_KEY_REFRESH),
    };
  } catch {
    return { accessToken: null, refreshToken: null };
  }
}

// ---------------------------------------------------------------------------
// Initial state — rehydrate tokens from sessionStorage on page load
// ---------------------------------------------------------------------------

const { accessToken: persistedAccess, refreshToken: persistedRefresh } =
  loadPersistedTokens();

const initialState: AuthState = {
  accessToken: persistedAccess,
  refreshToken: persistedRefresh,
  // User identity is not persisted; it will be reloaded on the next API call
  // or the app will redirect to login when the access token expires.
  user: null,
  isAuthenticated: persistedAccess !== null,
  expiresAt: null,
};

// ---------------------------------------------------------------------------
// Slice
// ---------------------------------------------------------------------------

const authSlice = createSlice({
  name: 'auth',
  initialState,

  reducers: {
    /**
     * Called after a successful login or token refresh.
     * Stores tokens in state AND persists them to sessionStorage.
     */
    setCredentials(state, action: PayloadAction<SetCredentialsPayload>) {
      const { accessToken, refreshToken, user, expiresAt } = action.payload;

      state.accessToken = accessToken;
      state.refreshToken = refreshToken;
      state.user = user;
      state.isAuthenticated = true;
      state.expiresAt = expiresAt;

      persistTokens(accessToken, refreshToken);
    },

    /**
     * Called on logout or when a refresh-token exchange fails.
     * Clears all auth state and removes tokens from sessionStorage.
     */
    clearCredentials(state) {
      state.accessToken = null;
      state.refreshToken = null;
      state.user = null;
      state.isAuthenticated = false;
      state.expiresAt = null;

      clearPersistedTokens();
    },
  },
});

// ---------------------------------------------------------------------------
// Exports
// ---------------------------------------------------------------------------

export const { setCredentials, clearCredentials } = authSlice.actions;

// Selectors
export const selectIsAuthenticated = (state: RootState): boolean =>
  state.auth.isAuthenticated;

export const selectCurrentUser = (state: RootState): AuthUser | null =>
  state.auth.user;

export const selectAccessToken = (state: RootState): string | null =>
  state.auth.accessToken;

export const selectRefreshToken = (state: RootState): string | null =>
  state.auth.refreshToken;

export default authSlice.reducer;
