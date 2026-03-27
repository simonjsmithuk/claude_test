/**
 * hooks.ts
 * ========
 * Pre-typed Redux hooks for use throughout the application.
 *
 * Import these instead of the plain `useDispatch` / `useSelector` from
 * react-redux to get full TypeScript inference over the RootState shape
 * and the AppDispatch type (which includes RTK Query dispatch types).
 *
 * Usage:
 *   import { useAppDispatch, useAppSelector } from '../redux/hooks';
 *
 *   const dispatch = useAppDispatch();
 *   const user = useAppSelector(selectCurrentUser);
 */

import { useDispatch, useSelector } from 'react-redux';
import type { AppDispatch, RootState } from './store';

/**
 * Typed version of `useDispatch`.
 * Ensures thunks and RTK Query mutations infer the correct dispatch type.
 */
export const useAppDispatch = useDispatch.withTypes<AppDispatch>();

/**
 * Typed version of `useSelector`.
 * Infers the state type automatically — no need to annotate the selector.
 */
export const useAppSelector = useSelector.withTypes<RootState>();
