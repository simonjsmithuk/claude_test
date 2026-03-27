/**
 * setupTests.ts
 * =============
 * Jest global setup file — runs once after the test framework is installed
 * but before any test suite executes.
 *
 * Responsibilities:
 *  - Clear sessionStorage before every test so persisted tokens from one
 *    test never bleed into another (authSlice reads sessionStorage at
 *    module initialisation time).
 *  - Restore all jest.spyOn mocks after each test automatically
 *    (jest.config.ts also sets restoreMocks: true for safety).
 */

// ---------------------------------------------------------------------------
// sessionStorage reset
// ---------------------------------------------------------------------------
// jsdom provides a real (in-memory) sessionStorage implementation.
// Clearing it before each test is the safest way to guarantee isolation
// because authSlice reads sessionStorage at module-load time — if the module
// is already cached by Jest, the only reliable reset mechanism is to clear
// the backing storage before every test.

beforeEach(() => {
  sessionStorage.clear();
  localStorage.clear();
});

// ---------------------------------------------------------------------------
// Console noise suppression (optional)
// ---------------------------------------------------------------------------
// Uncomment the block below if RTK generates noisy middleware warnings
// during tests that you want to suppress.
//
// beforeAll(() => {
//   jest.spyOn(console, 'warn').mockImplementation(() => {});
//   jest.spyOn(console, 'error').mockImplementation(() => {});
// });
//
// afterAll(() => {
//   jest.restoreAllMocks();
// });
