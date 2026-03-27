/**
 * store.ts
 * ========
 * Redux Toolkit store configuration for the DataViewer SPA.
 *
 * Combines all feature slices and the RTK Query API reducer.
 * The store type exports (RootState, AppDispatch, AppStore) are used
 * throughout the application for typed hooks and selector functions.
 */

import { configureStore } from '@reduxjs/toolkit';
import { dataViewerApi } from './api/dataViewerApi';
import authReducer from './slices/authSlice';
import credentialProfilesReducer from './slices/credentialProfilesSlice';
import transactionsReducer from './slices/transactionsSlice';
import preferencesReducer from './slices/preferencesSlice';
import auditReducer from './slices/auditSlice';
import uiReducer from './slices/uiSlice';

export const store = configureStore({
  reducer: {
    // Feature slices
    auth: authReducer,
    credentialProfiles: credentialProfilesReducer,
    transactions: transactionsReducer,
    preferences: preferencesReducer,
    audit: auditReducer,
    ui: uiReducer,

    // RTK Query cache reducer — must use the api's reducerPath as the key
    [dataViewerApi.reducerPath]: dataViewerApi.reducer,
  },

  middleware: (getDefaultMiddleware) =>
    getDefaultMiddleware().concat(dataViewerApi.middleware),
});

// ---------------------------------------------------------------------------
// Type exports
// ---------------------------------------------------------------------------

/** The root state shape — derived from the store so it always stays in sync. */
export type RootState = ReturnType<typeof store.getState>;

/** Typed dispatch — includes RTK Query dispatch types. */
export type AppDispatch = typeof store.dispatch;

/** Typed store instance (used by apiClient.ts for store injection). */
export type AppStore = typeof store;
