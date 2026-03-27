/**
 * uiSlice.test.ts
 * ===============
 * Unit tests for the ui Redux slice.
 *
 * Coverage targets:
 *  - Initial state shape
 *  - setLoading: toggles the global loading overlay
 *  - setError: sets errorMessage, clears successMessage
 *  - setSuccess: sets successMessage, clears errorMessage
 *  - clearMessages: clears both messages
 *  - resetUi: returns the full initial state object
 *  - All selectors including selectHasNotification
 *  - Edge cases: empty string messages, repeated dispatches, ordering of clears
 */

import uiReducer, {
  setLoading,
  setError,
  setSuccess,
  clearMessages,
  resetUi,
  selectIsLoading,
  selectErrorMessage,
  selectSuccessMessage,
  selectHasNotification,
  type UiState,
} from '../uiSlice';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeInitialState(): UiState {
  return uiReducer(undefined, { type: '@@INIT' });
}

function makeRootState(state: UiState) {
  return { ui: state } as { ui: UiState };
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('uiSlice', () => {
  // =========================================================================
  // Initial state
  // =========================================================================

  describe('initial state', () => {
    it('has isLoading set to false', () => {
      expect(makeInitialState().isLoading).toBe(false);
    });

    it('has errorMessage set to null', () => {
      expect(makeInitialState().errorMessage).toBeNull();
    });

    it('has successMessage set to null', () => {
      expect(makeInitialState().successMessage).toBeNull();
    });
  });

  // =========================================================================
  // setLoading
  // =========================================================================

  describe('setLoading', () => {
    it('sets isLoading to true', () => {
      const state = uiReducer(makeInitialState(), setLoading(true));

      expect(state.isLoading).toBe(true);
    });

    it('sets isLoading to false', () => {
      let state = uiReducer(makeInitialState(), setLoading(true));
      state = uiReducer(state, setLoading(false));

      expect(state.isLoading).toBe(false);
    });

    it('does not affect errorMessage when toggling loading', () => {
      let state = uiReducer(makeInitialState(), setError('Something failed'));
      state = uiReducer(state, setLoading(true));

      expect(state.errorMessage).toBe('Something failed');
    });

    it('does not affect successMessage when toggling loading', () => {
      let state = uiReducer(makeInitialState(), setSuccess('Saved!'));
      state = uiReducer(state, setLoading(true));

      expect(state.successMessage).toBe('Saved!');
    });

    it('is idempotent — setting true twice leaves isLoading true', () => {
      let state = uiReducer(makeInitialState(), setLoading(true));
      state = uiReducer(state, setLoading(true));

      expect(state.isLoading).toBe(true);
    });
  });

  // =========================================================================
  // setError
  // =========================================================================

  describe('setError', () => {
    it('stores the error message', () => {
      const state = uiReducer(
        makeInitialState(),
        setError('Network error: request timed out'),
      );

      expect(state.errorMessage).toBe('Network error: request timed out');
    });

    it('clears any existing successMessage', () => {
      let state = uiReducer(makeInitialState(), setSuccess('Profile saved'));
      state = uiReducer(state, setError('Save failed'));

      expect(state.successMessage).toBeNull();
    });

    it('replaces a previous error message with a new one', () => {
      let state = uiReducer(makeInitialState(), setError('Error 1'));
      state = uiReducer(state, setError('Error 2'));

      expect(state.errorMessage).toBe('Error 2');
    });

    it('does not affect isLoading', () => {
      let state = uiReducer(makeInitialState(), setLoading(true));
      state = uiReducer(state, setError('Oops'));

      expect(state.isLoading).toBe(true);
    });

    it('accepts an empty string as an error message (edge case)', () => {
      const state = uiReducer(makeInitialState(), setError(''));

      expect(state.errorMessage).toBe('');
    });

    it('accepts a long error message', () => {
      const longMsg = 'E'.repeat(1000);
      const state = uiReducer(makeInitialState(), setError(longMsg));

      expect(state.errorMessage).toBe(longMsg);
    });

    it('accepts special characters in the error message', () => {
      const special = 'Error: <script>alert("xss")</script> & "quotes"';
      const state = uiReducer(makeInitialState(), setError(special));

      expect(state.errorMessage).toBe(special);
    });
  });

  // =========================================================================
  // setSuccess
  // =========================================================================

  describe('setSuccess', () => {
    it('stores the success message', () => {
      const state = uiReducer(makeInitialState(), setSuccess('Profile created successfully'));

      expect(state.successMessage).toBe('Profile created successfully');
    });

    it('clears any existing errorMessage', () => {
      let state = uiReducer(makeInitialState(), setError('Previous error'));
      state = uiReducer(state, setSuccess('Now it worked'));

      expect(state.errorMessage).toBeNull();
    });

    it('replaces a previous success message with a new one', () => {
      let state = uiReducer(makeInitialState(), setSuccess('Step 1 done'));
      state = uiReducer(state, setSuccess('Step 2 done'));

      expect(state.successMessage).toBe('Step 2 done');
    });

    it('does not affect isLoading', () => {
      let state = uiReducer(makeInitialState(), setLoading(true));
      state = uiReducer(state, setSuccess('Done!'));

      expect(state.isLoading).toBe(true);
    });

    it('accepts an empty string as a success message (edge case)', () => {
      const state = uiReducer(makeInitialState(), setSuccess(''));

      expect(state.successMessage).toBe('');
    });
  });

  // =========================================================================
  // setError and setSuccess mutual exclusivity
  // =========================================================================

  describe('setError / setSuccess mutual exclusivity', () => {
    it('error → success leaves only successMessage', () => {
      let state = uiReducer(makeInitialState(), setError('Something failed'));
      state = uiReducer(state, setSuccess('Recovered'));

      expect(state.errorMessage).toBeNull();
      expect(state.successMessage).toBe('Recovered');
    });

    it('success → error leaves only errorMessage', () => {
      let state = uiReducer(makeInitialState(), setSuccess('Saved!'));
      state = uiReducer(state, setError('Failed!'));

      expect(state.successMessage).toBeNull();
      expect(state.errorMessage).toBe('Failed!');
    });
  });

  // =========================================================================
  // clearMessages
  // =========================================================================

  describe('clearMessages', () => {
    it('clears errorMessage', () => {
      let state = uiReducer(makeInitialState(), setError('Error!'));
      state = uiReducer(state, clearMessages());

      expect(state.errorMessage).toBeNull();
    });

    it('clears successMessage', () => {
      let state = uiReducer(makeInitialState(), setSuccess('Success!'));
      state = uiReducer(state, clearMessages());

      expect(state.successMessage).toBeNull();
    });

    it('clears both messages simultaneously', () => {
      // Simulate a scenario where both exist (though the slice normally
      // prevents this — we set state manually to be thorough).
      const stateWithBoth: UiState = {
        isLoading: false,
        errorMessage: 'Error',
        successMessage: 'Success',
      };
      const state = uiReducer(stateWithBoth, clearMessages());

      expect(state.errorMessage).toBeNull();
      expect(state.successMessage).toBeNull();
    });

    it('does not affect isLoading', () => {
      let state = uiReducer(makeInitialState(), setLoading(true));
      state = uiReducer(state, setError('Err'));
      state = uiReducer(state, clearMessages());

      expect(state.isLoading).toBe(true);
    });

    it('is a no-op when no messages are set (no errors thrown)', () => {
      expect(() => {
        uiReducer(makeInitialState(), clearMessages());
      }).not.toThrow();

      const state = uiReducer(makeInitialState(), clearMessages());
      expect(state.errorMessage).toBeNull();
      expect(state.successMessage).toBeNull();
    });
  });

  // =========================================================================
  // resetUi
  // =========================================================================

  describe('resetUi', () => {
    it('resets isLoading to false', () => {
      let state = uiReducer(makeInitialState(), setLoading(true));
      state = uiReducer(state, resetUi());

      expect(state.isLoading).toBe(false);
    });

    it('resets errorMessage to null', () => {
      let state = uiReducer(makeInitialState(), setError('Critical error'));
      state = uiReducer(state, resetUi());

      expect(state.errorMessage).toBeNull();
    });

    it('resets successMessage to null', () => {
      let state = uiReducer(makeInitialState(), setSuccess('Logged out'));
      state = uiReducer(state, resetUi());

      expect(state.successMessage).toBeNull();
    });

    it('returns a state structurally equal to the initial state', () => {
      let state = uiReducer(makeInitialState(), setLoading(true));
      state = uiReducer(state, setError('Something went wrong'));
      state = uiReducer(state, resetUi());

      expect(state).toEqual(makeInitialState());
    });

    it('is idempotent when called multiple times', () => {
      let state = uiReducer(makeInitialState(), resetUi());
      state = uiReducer(state, resetUi());

      expect(state).toEqual(makeInitialState());
    });
  });

  // =========================================================================
  // State immutability
  // =========================================================================

  describe('state immutability', () => {
    it('does not mutate the previous state reference on setLoading', () => {
      const before = makeInitialState();
      const after = uiReducer(before, setLoading(true));

      expect(after).not.toBe(before);
    });

    it('does not mutate the previous state on setError', () => {
      const before = makeInitialState();
      const after = uiReducer(before, setError('Error'));

      expect(after).not.toBe(before);
    });

    it('does not mutate the previous state on setSuccess', () => {
      const before = makeInitialState();
      const after = uiReducer(before, setSuccess('Success'));

      expect(after).not.toBe(before);
    });

    it('does not mutate the previous state on clearMessages', () => {
      const before = uiReducer(makeInitialState(), setError('Error'));
      const after = uiReducer(before, clearMessages());

      expect(after).not.toBe(before);
    });
  });

  // =========================================================================
  // Selectors
  // =========================================================================

  describe('selectors', () => {
    it('selectIsLoading returns false from initial state', () => {
      const root = makeRootState(makeInitialState());

      expect(selectIsLoading(root as any)).toBe(false);
    });

    it('selectIsLoading returns true after setLoading(true)', () => {
      const state = uiReducer(makeInitialState(), setLoading(true));
      const root = makeRootState(state);

      expect(selectIsLoading(root as any)).toBe(true);
    });

    it('selectErrorMessage returns null from initial state', () => {
      const root = makeRootState(makeInitialState());

      expect(selectErrorMessage(root as any)).toBeNull();
    });

    it('selectErrorMessage returns the error string after setError', () => {
      const state = uiReducer(makeInitialState(), setError('Boom'));
      const root = makeRootState(state);

      expect(selectErrorMessage(root as any)).toBe('Boom');
    });

    it('selectErrorMessage returns null after clearMessages', () => {
      let state = uiReducer(makeInitialState(), setError('Err'));
      state = uiReducer(state, clearMessages());
      const root = makeRootState(state);

      expect(selectErrorMessage(root as any)).toBeNull();
    });

    it('selectSuccessMessage returns null from initial state', () => {
      const root = makeRootState(makeInitialState());

      expect(selectSuccessMessage(root as any)).toBeNull();
    });

    it('selectSuccessMessage returns the string after setSuccess', () => {
      const state = uiReducer(makeInitialState(), setSuccess('Done'));
      const root = makeRootState(state);

      expect(selectSuccessMessage(root as any)).toBe('Done');
    });

    it('selectSuccessMessage returns null after clearMessages', () => {
      let state = uiReducer(makeInitialState(), setSuccess('Done'));
      state = uiReducer(state, clearMessages());
      const root = makeRootState(state);

      expect(selectSuccessMessage(root as any)).toBeNull();
    });

    describe('selectHasNotification', () => {
      it('returns false when both messages are null', () => {
        const root = makeRootState(makeInitialState());

        expect(selectHasNotification(root as any)).toBe(false);
      });

      it('returns true when errorMessage is set', () => {
        const state = uiReducer(makeInitialState(), setError('Err'));
        const root = makeRootState(state);

        expect(selectHasNotification(root as any)).toBe(true);
      });

      it('returns true when successMessage is set', () => {
        const state = uiReducer(makeInitialState(), setSuccess('Ok'));
        const root = makeRootState(state);

        expect(selectHasNotification(root as any)).toBe(true);
      });

      it('returns true when both messages are set (defensive: manually crafted state)', () => {
        const stateWithBoth: UiState = {
          isLoading: false,
          errorMessage: 'Error',
          successMessage: 'Success',
        };
        const root = makeRootState(stateWithBoth);

        expect(selectHasNotification(root as any)).toBe(true);
      });

      it('returns false after clearMessages has been dispatched', () => {
        let state = uiReducer(makeInitialState(), setError('Err'));
        state = uiReducer(state, clearMessages());
        const root = makeRootState(state);

        expect(selectHasNotification(root as any)).toBe(false);
      });

      it('returns false after resetUi', () => {
        let state = uiReducer(makeInitialState(), setSuccess('Bye'));
        state = uiReducer(state, resetUi());
        const root = makeRootState(state);

        expect(selectHasNotification(root as any)).toBe(false);
      });
    });
  });
});
