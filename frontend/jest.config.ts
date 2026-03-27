/**
 * jest.config.ts
 * ==============
 * Jest configuration for the DataViewer frontend test suite.
 *
 * Stack:
 *  - Jest 29+ with ts-jest for TypeScript transformation
 *  - jsdom environment for DOM/sessionStorage/localStorage access
 *  - Module name mapper to resolve @ path aliases defined in tsconfig.json
 */

import type { Config } from 'jest';

const config: Config = {
  // -------------------------------------------------------------------------
  // Test environment
  // -------------------------------------------------------------------------
  // jsdom gives us a browser-like environment with sessionStorage, window, etc.
  testEnvironment: 'jest-environment-jsdom',

  // -------------------------------------------------------------------------
  // Transform
  // -------------------------------------------------------------------------
  // ts-jest transpiles TypeScript files before Jest runs them.
  // The `isolatedModules` flag speeds up transformation significantly.
  transform: {
    '^.+\\.tsx?$': [
      'ts-jest',
      {
        tsconfig: {
          // Ensure strict mode is active (mirrors production tsconfig).
          strict: true,
          // Allow synthetic default imports (required by React + RTK).
          esModuleInterop: true,
          jsx: 'react-jsx',
        },
        isolatedModules: true,
      },
    ],
  },

  // -------------------------------------------------------------------------
  // Module resolution
  // -------------------------------------------------------------------------
  moduleNameMapper: {
    // Support absolute imports via the @ alias (e.g. "@/types/api.types").
    '^@/(.*)$': '<rootDir>/src/$1',
  },

  // -------------------------------------------------------------------------
  // Test file patterns
  // -------------------------------------------------------------------------
  testMatch: [
    // Picks up *.test.ts(x) and *.spec.ts(x) anywhere under src.
    '<rootDir>/src/**/__tests__/**/*.{ts,tsx}',
    '<rootDir>/src/**/*.{test,spec}.{ts,tsx}',
  ],

  // -------------------------------------------------------------------------
  // Setup files
  // -------------------------------------------------------------------------
  // Runs after the Jest test framework is installed in the environment
  // (equivalent to CRA's src/setupTests.ts convention).
  setupFilesAfterFramework: ['<rootDir>/src/setupTests.ts'],

  // -------------------------------------------------------------------------
  // Coverage
  // -------------------------------------------------------------------------
  collectCoverageFrom: [
    'src/redux/slices/**/*.{ts,tsx}',
    // Exclude barrel index files — no logic to cover.
    '!src/redux/slices/index.ts',
    '!src/**/*.d.ts',
  ],

  coverageThresholds: {
    // Enforce ≥80% line coverage as required by the acceptance criteria.
    global: {
      lines: 80,
      functions: 80,
      branches: 75,
      statements: 80,
    },
  },

  coverageReporters: ['text', 'lcov', 'html'],

  // -------------------------------------------------------------------------
  // Miscellaneous
  // -------------------------------------------------------------------------
  // Clear mocks between tests to prevent cross-test contamination.
  clearMocks: true,
  restoreMocks: true,

  // Verbose output shows each test name in the terminal.
  verbose: true,
};

export default config;
