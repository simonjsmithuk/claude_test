/**
 * preferencesSlice.test.ts
 * ========================
 * Unit tests for the preferences Redux slice.
 *
 * Coverage targets:
 *  - Initial state (preferences null, isLoading false)
 *  - PREFERENCE_DEFAULTS exported constant
 *  - setPreferences: stores the full DTO
 *  - patchPreferences: merges partial DTO, no-ops when preferences is null
 *  - setPreferencesLoading: toggles loading flag
 *  - clearPreferences: full reset
 *  - All selectors including selectPreferencesWithDefaults and derived selectors
 *  - Edge cases: null preferredProfileId, boundary page sizes
 */

import preferencesReducer, {
  setPreferences,
  patchPreferences,
  setPreferencesLoading,
  clearPreferences,
  PREFERENCE_DEFAULTS,
  selectPreferences,
  selectPreferencesWithDefaults,
  selectPreferencesIsLoading,
  selectDefaultPageSize,
  selectDefaultDateRangeDays,
  selectPreferredProfileId,
  type PreferencesState,
} from '../preferencesSlice';
import type { UserPreferenceDto } from '../../../types/api.types';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const basePreferences: UserPreferenceDto = {
  defaultPageSize: 50,
  defaultDateRangeDays: 30,
  preferredProfileId: 'prof-abc',
};

const minimalPreferences: UserPreferenceDto = {
  defaultPageSize: 10,
  defaultDateRangeDays: 1,
  preferredProfileId: null,
};

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeInitialState(): PreferencesState {
  return preferencesReducer(undefined, { type: '@@INIT' });
}

function makeRootState(state: PreferencesState) {
  return { preferences: state } as { preferences: PreferencesState };
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('preferencesSlice', () => {
  // =========================================================================
  // PREFERENCE_DEFAULTS constant
  // =========================================================================

  describe('PREFERENCE_DEFAULTS', () => {
    it('has defaultPageSize of 20', () => {
      expect(PREFERENCE_DEFAULTS.defaultPageSize).toBe(20);
    });

    it('has defaultDateRangeDays of 7', () => {
      expect(PREFERENCE_DEFAULTS.defaultDateRangeDays).toBe(7);
    });

    it('has preferredProfileId of null', () => {
      expect(PREFERENCE_DEFAULTS.preferredProfileId).toBeNull();
    });
  });

  // =========================================================================
  // Initial state
  // =========================================================================

  describe('initial state', () => {
    it('has preferences set to null', () => {
      expect(makeInitialState().preferences).toBeNull();
    });

    it('has isLoading set to false', () => {
      expect(makeInitialState().isLoading).toBe(false);
    });
  });

  // =========================================================================
  // setPreferences
  // =========================================================================

  describe('setPreferences', () => {
    it('stores the full UserPreferenceDto in state', () => {
      const state = preferencesReducer(
        makeInitialState(),
        setPreferences(basePreferences),
      );

      expect(state.preferences).toEqual(basePreferences);
    });

    it('stores defaultPageSize correctly', () => {
      const state = preferencesReducer(makeInitialState(), setPreferences(basePreferences));

      expect(state.preferences?.defaultPageSize).toBe(50);
    });

    it('stores defaultDateRangeDays correctly', () => {
      const state = preferencesReducer(makeInitialState(), setPreferences(basePreferences));

      expect(state.preferences?.defaultDateRangeDays).toBe(30);
    });

    it('stores preferredProfileId correctly', () => {
      const state = preferencesReducer(makeInitialState(), setPreferences(basePreferences));

      expect(state.preferences?.preferredProfileId).toBe('prof-abc');
    });

    it('stores null preferredProfileId', () => {
      const state = preferencesReducer(
        makeInitialState(),
        setPreferences(minimalPreferences),
      );

      expect(state.preferences?.preferredProfileId).toBeNull();
    });

    it('replaces previously stored preferences with new ones', () => {
      let state = preferencesReducer(makeInitialState(), setPreferences(basePreferences));
      state = preferencesReducer(state, setPreferences(minimalPreferences));

      expect(state.preferences).toEqual(minimalPreferences);
    });

    it('accepts boundary page size of 1', () => {
      const tiny: UserPreferenceDto = {
        defaultPageSize: 1,
        defaultDateRangeDays: 1,
        preferredProfileId: null,
      };
      const state = preferencesReducer(makeInitialState(), setPreferences(tiny));

      expect(state.preferences?.defaultPageSize).toBe(1);
    });

    it('accepts a large page size (boundary value)', () => {
      const large: UserPreferenceDto = {
        defaultPageSize: 100,
        defaultDateRangeDays: 365,
        preferredProfileId: null,
      };
      const state = preferencesReducer(makeInitialState(), setPreferences(large));

      expect(state.preferences?.defaultPageSize).toBe(100);
      expect(state.preferences?.defaultDateRangeDays).toBe(365);
    });
  });

  // =========================================================================
  // patchPreferences
  // =========================================================================

  describe('patchPreferences', () => {
    it('merges a partial update into existing preferences', () => {
      let state = preferencesReducer(makeInitialState(), setPreferences(basePreferences));
      state = preferencesReducer(state, patchPreferences({ defaultPageSize: 10 }));

      expect(state.preferences?.defaultPageSize).toBe(10);
      expect(state.preferences?.defaultDateRangeDays).toBe(30); // unchanged
      expect(state.preferences?.preferredProfileId).toBe('prof-abc'); // unchanged
    });

    it('can patch preferredProfileId to null', () => {
      let state = preferencesReducer(makeInitialState(), setPreferences(basePreferences));
      state = preferencesReducer(state, patchPreferences({ preferredProfileId: null }));

      expect(state.preferences?.preferredProfileId).toBeNull();
      expect(state.preferences?.defaultPageSize).toBe(50); // unchanged
    });

    it('can patch all fields simultaneously', () => {
      let state = preferencesReducer(makeInitialState(), setPreferences(basePreferences));
      const patch: Partial<UserPreferenceDto> = {
        defaultPageSize: 25,
        defaultDateRangeDays: 14,
        preferredProfileId: 'prof-new',
      };
      state = preferencesReducer(state, patchPreferences(patch));

      expect(state.preferences).toEqual({
        defaultPageSize: 25,
        defaultDateRangeDays: 14,
        preferredProfileId: 'prof-new',
      });
    });

    it('is a no-op (does not crash) when preferences is null', () => {
      const state = preferencesReducer(
        makeInitialState(),
        patchPreferences({ defaultPageSize: 10 }),
      );

      // preferences stays null — there is nothing to merge into
      expect(state.preferences).toBeNull();
    });

    it('applying an empty patch leaves preferences unchanged', () => {
      let state = preferencesReducer(makeInitialState(), setPreferences(basePreferences));
      state = preferencesReducer(state, patchPreferences({}));

      expect(state.preferences).toEqual(basePreferences);
    });
  });

  // =========================================================================
  // setPreferencesLoading
  // =========================================================================

  describe('setPreferencesLoading', () => {
    it('sets isLoading to true', () => {
      const state = preferencesReducer(
        makeInitialState(),
        setPreferencesLoading(true),
      );

      expect(state.isLoading).toBe(true);
    });

    it('sets isLoading back to false', () => {
      let state = preferencesReducer(makeInitialState(), setPreferencesLoading(true));
      state = preferencesReducer(state, setPreferencesLoading(false));

      expect(state.isLoading).toBe(false);
    });

    it('does not affect preferences when toggling loading', () => {
      let state = preferencesReducer(makeInitialState(), setPreferences(basePreferences));
      state = preferencesReducer(state, setPreferencesLoading(true));

      expect(state.preferences).toEqual(basePreferences);
    });
  });

  // =========================================================================
  // clearPreferences
  // =========================================================================

  describe('clearPreferences', () => {
    it('sets preferences back to null', () => {
      let state = preferencesReducer(makeInitialState(), setPreferences(basePreferences));
      state = preferencesReducer(state, clearPreferences());

      expect(state.preferences).toBeNull();
    });

    it('sets isLoading back to false', () => {
      let state = preferencesReducer(makeInitialState(), setPreferencesLoading(true));
      state = preferencesReducer(state, clearPreferences());

      expect(state.isLoading).toBe(false);
    });

    it('is idempotent — calling twice leaves state at initial', () => {
      let state = preferencesReducer(makeInitialState(), setPreferences(basePreferences));
      state = preferencesReducer(state, clearPreferences());
      state = preferencesReducer(state, clearPreferences());

      expect(state.preferences).toBeNull();
      expect(state.isLoading).toBe(false);
    });
  });

  // =========================================================================
  // State immutability
  // =========================================================================

  describe('state immutability', () => {
    it('does not mutate the previous state on setPreferences', () => {
      const before = makeInitialState();
      const after = preferencesReducer(before, setPreferences(basePreferences));

      expect(after).not.toBe(before);
    });

    it('does not mutate the previous state on patchPreferences', () => {
      const withPrefs = preferencesReducer(
        makeInitialState(),
        setPreferences(basePreferences),
      );
      const after = preferencesReducer(
        withPrefs,
        patchPreferences({ defaultPageSize: 99 }),
      );

      expect(after).not.toBe(withPrefs);
    });
  });

  // =========================================================================
  // Selectors
  // =========================================================================

  describe('selectors', () => {
    it('selectPreferences returns null from initial state', () => {
      const root = makeRootState(makeInitialState());

      expect(selectPreferences(root as any)).toBeNull();
    });

    it('selectPreferences returns the stored DTO after setPreferences', () => {
      const state = preferencesReducer(makeInitialState(), setPreferences(basePreferences));
      const root = makeRootState(state);

      expect(selectPreferences(root as any)).toEqual(basePreferences);
    });

    it('selectPreferencesWithDefaults returns PREFERENCE_DEFAULTS when preferences is null', () => {
      const root = makeRootState(makeInitialState());
      const result = selectPreferencesWithDefaults(root as any);

      expect(result).toEqual(PREFERENCE_DEFAULTS);
    });

    it('selectPreferencesWithDefaults returns stored preferences when set', () => {
      const state = preferencesReducer(makeInitialState(), setPreferences(basePreferences));
      const root = makeRootState(state);
      const result = selectPreferencesWithDefaults(root as any);

      expect(result).toEqual(basePreferences);
    });

    it('selectPreferencesIsLoading returns false initially', () => {
      const root = makeRootState(makeInitialState());

      expect(selectPreferencesIsLoading(root as any)).toBe(false);
    });

    it('selectPreferencesIsLoading returns true after setPreferencesLoading(true)', () => {
      const state = preferencesReducer(
        makeInitialState(),
        setPreferencesLoading(true),
      );
      const root = makeRootState(state);

      expect(selectPreferencesIsLoading(root as any)).toBe(true);
    });

    it('selectDefaultPageSize returns PREFERENCE_DEFAULTS.defaultPageSize when no preferences', () => {
      const root = makeRootState(makeInitialState());

      expect(selectDefaultPageSize(root as any)).toBe(
        PREFERENCE_DEFAULTS.defaultPageSize,
      );
    });

    it('selectDefaultPageSize returns the stored value when preferences exist', () => {
      const state = preferencesReducer(makeInitialState(), setPreferences(basePreferences));
      const root = makeRootState(state);

      expect(selectDefaultPageSize(root as any)).toBe(50);
    });

    it('selectDefaultDateRangeDays returns PREFERENCE_DEFAULTS.defaultDateRangeDays when no preferences', () => {
      const root = makeRootState(makeInitialState());

      expect(selectDefaultDateRangeDays(root as any)).toBe(
        PREFERENCE_DEFAULTS.defaultDateRangeDays,
      );
    });

    it('selectDefaultDateRangeDays returns the stored value when preferences exist', () => {
      const state = preferencesReducer(makeInitialState(), setPreferences(basePreferences));
      const root = makeRootState(state);

      expect(selectDefaultDateRangeDays(root as any)).toBe(30);
    });

    it('selectPreferredProfileId returns null from initial state', () => {
      const root = makeRootState(makeInitialState());

      expect(selectPreferredProfileId(root as any)).toBeNull();
    });

    it('selectPreferredProfileId returns null when preferredProfileId is null', () => {
      const state = preferencesReducer(
        makeInitialState(),
        setPreferences(minimalPreferences),
      );
      const root = makeRootState(state);

      expect(selectPreferredProfileId(root as any)).toBeNull();
    });

    it('selectPreferredProfileId returns the profile id when set', () => {
      const state = preferencesReducer(makeInitialState(), setPreferences(basePreferences));
      const root = makeRootState(state);

      expect(selectPreferredProfileId(root as any)).toBe('prof-abc');
    });
  });
});
