/**
 * credentialProfilesSlice.test.ts
 * ================================
 * Unit tests for the credentialProfiles Redux slice.
 *
 * Coverage targets:
 *  - Initial state shape
 *  - setProfiles: replaces list, derives activeProfileId
 *  - upsertProfile: insert new / replace existing, active-flag sync
 *  - removeProfile: filtering, activeProfileId reset, testConnectionStatus cleanup
 *  - setActiveProfileId: updates id and mirrors isActive flags
 *  - setTestConnectionPending / setTestConnectionResult / resetTestConnectionStatus
 *  - clearProfiles: full reset
 *  - All selectors (including curried selectTestConnectionStatus)
 *  - Edge cases: empty payloads, unknown profileIds, isDeleted profiles
 */

import credentialProfilesReducer, {
  setProfiles,
  upsertProfile,
  removeProfile,
  setActiveProfileId,
  setTestConnectionPending,
  setTestConnectionResult,
  resetTestConnectionStatus,
  clearProfiles,
  selectAllProfiles,
  selectActiveProfileId,
  selectActiveProfile,
  selectTestConnectionStatus,
  type CredentialProfilesState,
} from '../credentialProfilesSlice';
import type { CredentialProfileDto, TestConnectionResult } from '../../../types/api.types';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

function makeProfile(overrides: Partial<CredentialProfileDto> = {}): CredentialProfileDto {
  return {
    id: 'prof-001',
    name: 'Default Profile',
    accessKeyId: 'AKIA0000001',
    region: 'us-east-1',
    bucketName: 'my-bucket',
    keyPrefix: null,
    isActive: false,
    isDeleted: false,
    createdAt: '2025-01-01T00:00:00Z',
    updatedAt: '2025-01-01T00:00:00Z',
    ...overrides,
  };
}

const profileA = makeProfile({ id: 'prof-001', name: 'Profile A', isActive: false });
const profileB = makeProfile({ id: 'prof-002', name: 'Profile B', isActive: true });
const profileC = makeProfile({ id: 'prof-003', name: 'Profile C', isActive: false });

function makeSuccessResult(profileId: string): TestConnectionResult {
  return {
    success: true,
    message: 'Connected successfully',
    testedAt: '2025-07-10T10:00:00Z',
  };
}

function makeFailureResult(): TestConnectionResult {
  return {
    success: false,
    message: 'Access denied',
    testedAt: '2025-07-10T10:05:00Z',
  };
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeInitialState(): CredentialProfilesState {
  return credentialProfilesReducer(undefined, { type: '@@INIT' });
}

function makeRootState(state: CredentialProfilesState) {
  return { credentialProfiles: state } as { credentialProfiles: CredentialProfilesState };
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('credentialProfilesSlice', () => {
  // =========================================================================
  // Initial state
  // =========================================================================

  describe('initial state', () => {
    it('has an empty profiles list', () => {
      expect(makeInitialState().profiles).toEqual([]);
    });

    it('has activeProfileId set to null', () => {
      expect(makeInitialState().activeProfileId).toBeNull();
    });

    it('has an empty testConnectionStatus record', () => {
      expect(makeInitialState().testConnectionStatus).toEqual({});
    });
  });

  // =========================================================================
  // setProfiles
  // =========================================================================

  describe('setProfiles', () => {
    it('replaces the entire profiles list', () => {
      let state = makeInitialState();
      state = credentialProfilesReducer(state, setProfiles([profileA, profileB]));

      expect(state.profiles).toHaveLength(2);
      expect(state.profiles[0]).toEqual(profileA);
      expect(state.profiles[1]).toEqual(profileB);
    });

    it('derives activeProfileId from the active, non-deleted profile', () => {
      const state = credentialProfilesReducer(
        makeInitialState(),
        setProfiles([profileA, profileB]),
      );

      expect(state.activeProfileId).toBe('prof-002');
    });

    it('sets activeProfileId to null when no profile is active', () => {
      const state = credentialProfilesReducer(
        makeInitialState(),
        setProfiles([profileA, profileC]),
      );

      expect(state.activeProfileId).toBeNull();
    });

    it('ignores deleted profiles when deriving activeProfileId', () => {
      const deletedActive = makeProfile({
        id: 'prof-deleted',
        isActive: true,
        isDeleted: true,
      });
      const state = credentialProfilesReducer(
        makeInitialState(),
        setProfiles([deletedActive, profileA]),
      );

      expect(state.activeProfileId).toBeNull();
    });

    it('picks the first active non-deleted profile when multiple are active', () => {
      const twoActive = [
        makeProfile({ id: 'x1', isActive: true, isDeleted: false }),
        makeProfile({ id: 'x2', isActive: true, isDeleted: false }),
      ];
      const state = credentialProfilesReducer(makeInitialState(), setProfiles(twoActive));

      expect(state.activeProfileId).toBe('x1');
    });

    it('replaces an existing list with an empty array', () => {
      let state = credentialProfilesReducer(
        makeInitialState(),
        setProfiles([profileA, profileB]),
      );
      state = credentialProfilesReducer(state, setProfiles([]));

      expect(state.profiles).toEqual([]);
      expect(state.activeProfileId).toBeNull();
    });
  });

  // =========================================================================
  // upsertProfile
  // =========================================================================

  describe('upsertProfile', () => {
    it('appends a new profile when its id is not already in the list', () => {
      let state = credentialProfilesReducer(
        makeInitialState(),
        setProfiles([profileA]),
      );
      state = credentialProfilesReducer(state, upsertProfile(profileB));

      expect(state.profiles).toHaveLength(2);
      expect(state.profiles[1]).toEqual(profileB);
    });

    it('replaces an existing profile (same id) with the new value', () => {
      let state = credentialProfilesReducer(
        makeInitialState(),
        setProfiles([profileA, profileB]),
      );
      const updatedA = { ...profileA, name: 'Updated A' };
      state = credentialProfilesReducer(state, upsertProfile(updatedA));

      expect(state.profiles).toHaveLength(2);
      expect(state.profiles[0].name).toBe('Updated A');
    });

    it('updates activeProfileId when the upserted profile isActive', () => {
      let state = credentialProfilesReducer(
        makeInitialState(),
        setProfiles([profileA, profileB]),
      );
      const nowActive = { ...profileC, isActive: true };
      state = credentialProfilesReducer(state, upsertProfile(nowActive));

      expect(state.activeProfileId).toBe('prof-003');
    });

    it('does NOT update activeProfileId when the upserted profile is not active', () => {
      let state = credentialProfilesReducer(
        makeInitialState(),
        setProfiles([profileA, profileB]),
      );
      state = credentialProfilesReducer(state, upsertProfile(profileC)); // isActive: false

      expect(state.activeProfileId).toBe('prof-002'); // unchanged
    });
  });

  // =========================================================================
  // removeProfile
  // =========================================================================

  describe('removeProfile', () => {
    it('removes the profile with the given id from the list', () => {
      let state = credentialProfilesReducer(
        makeInitialState(),
        setProfiles([profileA, profileB, profileC]),
      );
      state = credentialProfilesReducer(state, removeProfile('prof-001'));

      expect(state.profiles.map((p) => p.id)).toEqual(['prof-002', 'prof-003']);
    });

    it('resets activeProfileId to null when the active profile is removed', () => {
      let state = credentialProfilesReducer(
        makeInitialState(),
        setProfiles([profileA, profileB]), // profileB is active
      );
      state = credentialProfilesReducer(state, removeProfile('prof-002'));

      expect(state.activeProfileId).toBeNull();
    });

    it('keeps activeProfileId unchanged when a non-active profile is removed', () => {
      let state = credentialProfilesReducer(
        makeInitialState(),
        setProfiles([profileA, profileB]),
      );
      state = credentialProfilesReducer(state, removeProfile('prof-001')); // profileA not active

      expect(state.activeProfileId).toBe('prof-002');
    });

    it('removes the testConnectionStatus entry for the deleted profile', () => {
      let state = credentialProfilesReducer(
        makeInitialState(),
        setProfiles([profileA, profileB]),
      );
      state = credentialProfilesReducer(
        state,
        setTestConnectionPending('prof-001'),
      );
      expect(state.testConnectionStatus['prof-001']).toBeDefined();

      state = credentialProfilesReducer(state, removeProfile('prof-001'));

      expect(state.testConnectionStatus['prof-001']).toBeUndefined();
    });

    it('is a no-op when the profile id does not exist', () => {
      let state = credentialProfilesReducer(
        makeInitialState(),
        setProfiles([profileA]),
      );
      state = credentialProfilesReducer(state, removeProfile('does-not-exist'));

      expect(state.profiles).toHaveLength(1);
    });
  });

  // =========================================================================
  // setActiveProfileId
  // =========================================================================

  describe('setActiveProfileId', () => {
    it('updates activeProfileId', () => {
      let state = credentialProfilesReducer(
        makeInitialState(),
        setProfiles([profileA, profileB, profileC]),
      );
      state = credentialProfilesReducer(state, setActiveProfileId('prof-003'));

      expect(state.activeProfileId).toBe('prof-003');
    });

    it('marks the target profile as isActive = true and others as false', () => {
      let state = credentialProfilesReducer(
        makeInitialState(),
        setProfiles([profileA, profileB, profileC]),
      );
      state = credentialProfilesReducer(state, setActiveProfileId('prof-001'));

      const flags = state.profiles.map((p) => ({ id: p.id, isActive: p.isActive }));
      expect(flags).toEqual([
        { id: 'prof-001', isActive: true },
        { id: 'prof-002', isActive: false },
        { id: 'prof-003', isActive: false },
      ]);
    });

    it('handles setting an id that is not in the profiles list (empty mirror)', () => {
      const state = credentialProfilesReducer(
        makeInitialState(),
        setActiveProfileId('unknown-id'),
      );

      expect(state.activeProfileId).toBe('unknown-id');
      expect(state.profiles).toEqual([]); // list unchanged
    });
  });

  // =========================================================================
  // setTestConnectionPending
  // =========================================================================

  describe('setTestConnectionPending', () => {
    it('sets the profile status to "pending"', () => {
      const state = credentialProfilesReducer(
        makeInitialState(),
        setTestConnectionPending('prof-001'),
      );

      expect(state.testConnectionStatus['prof-001'].status).toBe('pending');
    });

    it('clears result and lastTestedAt when marking pending', () => {
      const state = credentialProfilesReducer(
        makeInitialState(),
        setTestConnectionPending('prof-001'),
      );

      expect(state.testConnectionStatus['prof-001'].result).toBeNull();
      expect(state.testConnectionStatus['prof-001'].lastTestedAt).toBeNull();
    });

    it('creates separate status entries per profileId', () => {
      let state = makeInitialState();
      state = credentialProfilesReducer(state, setTestConnectionPending('prof-001'));
      state = credentialProfilesReducer(state, setTestConnectionPending('prof-002'));

      expect(state.testConnectionStatus['prof-001'].status).toBe('pending');
      expect(state.testConnectionStatus['prof-002'].status).toBe('pending');
    });
  });

  // =========================================================================
  // setTestConnectionResult
  // =========================================================================

  describe('setTestConnectionResult', () => {
    it('sets status to "success" for a successful result', () => {
      const result = makeSuccessResult('prof-001');
      const state = credentialProfilesReducer(
        makeInitialState(),
        setTestConnectionResult({ profileId: 'prof-001', result }),
      );

      expect(state.testConnectionStatus['prof-001'].status).toBe('success');
    });

    it('sets status to "failure" for an unsuccessful result', () => {
      const result = makeFailureResult();
      const state = credentialProfilesReducer(
        makeInitialState(),
        setTestConnectionResult({ profileId: 'prof-001', result }),
      );

      expect(state.testConnectionStatus['prof-001'].status).toBe('failure');
    });

    it('stores the full result object', () => {
      const result = makeSuccessResult('prof-001');
      const state = credentialProfilesReducer(
        makeInitialState(),
        setTestConnectionResult({ profileId: 'prof-001', result }),
      );

      expect(state.testConnectionStatus['prof-001'].result).toEqual(result);
    });

    it('sets lastTestedAt from result.testedAt', () => {
      const result = makeSuccessResult('prof-001');
      const state = credentialProfilesReducer(
        makeInitialState(),
        setTestConnectionResult({ profileId: 'prof-001', result }),
      );

      expect(state.testConnectionStatus['prof-001'].lastTestedAt).toBe(
        result.testedAt,
      );
    });

    it('can transition from pending → success without losing other entries', () => {
      let state = makeInitialState();
      state = credentialProfilesReducer(state, setTestConnectionPending('prof-002'));
      state = credentialProfilesReducer(
        state,
        setTestConnectionResult({
          profileId: 'prof-001',
          result: makeSuccessResult('prof-001'),
        }),
      );

      expect(state.testConnectionStatus['prof-002'].status).toBe('pending');
      expect(state.testConnectionStatus['prof-001'].status).toBe('success');
    });
  });

  // =========================================================================
  // resetTestConnectionStatus
  // =========================================================================

  describe('resetTestConnectionStatus', () => {
    it('resets the status back to "idle"', () => {
      let state = credentialProfilesReducer(
        makeInitialState(),
        setTestConnectionPending('prof-001'),
      );
      state = credentialProfilesReducer(state, resetTestConnectionStatus('prof-001'));

      expect(state.testConnectionStatus['prof-001'].status).toBe('idle');
    });

    it('clears result and lastTestedAt on reset', () => {
      let state = credentialProfilesReducer(
        makeInitialState(),
        setTestConnectionResult({
          profileId: 'prof-001',
          result: makeSuccessResult('prof-001'),
        }),
      );
      state = credentialProfilesReducer(state, resetTestConnectionStatus('prof-001'));

      expect(state.testConnectionStatus['prof-001'].result).toBeNull();
      expect(state.testConnectionStatus['prof-001'].lastTestedAt).toBeNull();
    });

    it('does not affect other profile statuses', () => {
      let state = makeInitialState();
      state = credentialProfilesReducer(state, setTestConnectionPending('prof-001'));
      state = credentialProfilesReducer(state, setTestConnectionPending('prof-002'));
      state = credentialProfilesReducer(state, resetTestConnectionStatus('prof-001'));

      expect(state.testConnectionStatus['prof-002'].status).toBe('pending');
    });
  });

  // =========================================================================
  // clearProfiles
  // =========================================================================

  describe('clearProfiles', () => {
    it('resets profiles to an empty array', () => {
      let state = credentialProfilesReducer(
        makeInitialState(),
        setProfiles([profileA, profileB]),
      );
      state = credentialProfilesReducer(state, clearProfiles());

      expect(state.profiles).toEqual([]);
    });

    it('resets activeProfileId to null', () => {
      let state = credentialProfilesReducer(
        makeInitialState(),
        setProfiles([profileA, profileB]),
      );
      state = credentialProfilesReducer(state, clearProfiles());

      expect(state.activeProfileId).toBeNull();
    });

    it('clears all testConnectionStatus entries', () => {
      let state = makeInitialState();
      state = credentialProfilesReducer(state, setTestConnectionPending('prof-001'));
      state = credentialProfilesReducer(state, clearProfiles());

      expect(state.testConnectionStatus).toEqual({});
    });
  });

  // =========================================================================
  // Selectors
  // =========================================================================

  describe('selectors', () => {
    let populatedState: CredentialProfilesState;

    beforeEach(() => {
      let s = makeInitialState();
      s = credentialProfilesReducer(s, setProfiles([profileA, profileB, profileC]));
      s = credentialProfilesReducer(
        s,
        setTestConnectionResult({
          profileId: 'prof-001',
          result: makeSuccessResult('prof-001'),
        }),
      );
      populatedState = s;
    });

    it('selectAllProfiles returns the full list', () => {
      const root = makeRootState(populatedState);
      expect(selectAllProfiles(root as any)).toHaveLength(3);
    });

    it('selectActiveProfileId returns the active profile id', () => {
      const root = makeRootState(populatedState);
      expect(selectActiveProfileId(root as any)).toBe('prof-002');
    });

    it('selectActiveProfileId returns null when no profile is active', () => {
      const emptyRoot = makeRootState(makeInitialState());
      expect(selectActiveProfileId(emptyRoot as any)).toBeNull();
    });

    it('selectActiveProfile returns the active CredentialProfileDto', () => {
      const root = makeRootState(populatedState);
      const active = selectActiveProfile(root as any);

      expect(active).toBeDefined();
      expect(active?.id).toBe('prof-002');
    });

    it('selectActiveProfile returns undefined when activeProfileId is null', () => {
      const root = makeRootState(makeInitialState());
      expect(selectActiveProfile(root as any)).toBeUndefined();
    });

    it('selectTestConnectionStatus returns the stored status for a known profile', () => {
      const root = makeRootState(populatedState);
      const status = selectTestConnectionStatus('prof-001')(root as any);

      expect(status.status).toBe('success');
      expect(status.result).not.toBeNull();
    });

    it('selectTestConnectionStatus returns idle default for an unknown profile', () => {
      const root = makeRootState(populatedState);
      const status = selectTestConnectionStatus('unknown-id')(root as any);

      expect(status.status).toBe('idle');
      expect(status.result).toBeNull();
      expect(status.lastTestedAt).toBeNull();
    });
  });
});
