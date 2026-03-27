/**
 * apiClient.ts
 * ============
 * Axios instance used by the RTK Query baseQuery.
 *
 * Responsibilities:
 *  1. Attach `Authorization: Bearer <accessToken>` on every request.
 *  2. On 401 responses, attempt a single token refresh then retry.
 *  3. On a second consecutive 401, dispatch `clearCredentials` to force logout.
 *
 * The store reference is injected lazily (via `injectStore`) to avoid the
 * circular import that would result from importing the store directly here
 * (store → api → apiClient → store).
 */

import axios, {
  AxiosError,
  AxiosInstance,
  AxiosResponse,
  InternalAxiosRequestConfig,
} from 'axios';
import type { AppStore } from '../redux/store';

// ---------------------------------------------------------------------------
// Store injection — avoids circular dependency with store.ts
// ---------------------------------------------------------------------------

let store: AppStore | null = null;

/**
 * Must be called once in main.tsx (or store.ts) after the store is created,
 * so that interceptors can read and update auth state.
 */
export function injectStore(appStore: AppStore): void {
  store = appStore;
}

// ---------------------------------------------------------------------------
// Axios instance
// ---------------------------------------------------------------------------

export const apiClient: AxiosInstance = axios.create({
  baseURL: '/api/v1',
  headers: {
    'Content-Type': 'application/json',
  },
  // Treat 4xx/5xx as resolved responses so RTK Query can inspect the status.
  validateStatus: () => true,
});

// ---------------------------------------------------------------------------
// Request interceptor — attach Bearer token
// ---------------------------------------------------------------------------

apiClient.interceptors.request.use(
  (config: InternalAxiosRequestConfig): InternalAxiosRequestConfig => {
    if (!store) return config;

    const token = store.getState().auth.accessToken;
    if (token && config.headers) {
      config.headers['Authorization'] = `Bearer ${token}`;
    }
    return config;
  },
  (error: unknown) => Promise.reject(error),
);

// ---------------------------------------------------------------------------
// Response interceptor — transparent token refresh on 401
// ---------------------------------------------------------------------------

// Flag to prevent concurrent refresh storms.
let isRefreshing = false;
// Queue of callbacks waiting for the new token.
let failedQueue: Array<{
  resolve: (token: string) => void;
  reject: (error: unknown) => void;
}> = [];

function processQueue(error: unknown, token: string | null): void {
  failedQueue.forEach(({ resolve, reject }) => {
    if (error) {
      reject(error);
    } else if (token) {
      resolve(token);
    }
  });
  failedQueue = [];
}

apiClient.interceptors.response.use(
  // Pass 2xx / non-401 responses straight through.
  (response: AxiosResponse) => response,

  async (error: AxiosError): Promise<AxiosResponse> => {
    const originalRequest = error.config as InternalAxiosRequestConfig & {
      _retry?: boolean;
    };

    // Only handle 401 on the first failure and only when we have a store.
    if (error.response?.status !== 401 || originalRequest._retry || !store) {
      return Promise.reject(error);
    }

    if (isRefreshing) {
      // Enqueue this request until the refresh completes.
      return new Promise<AxiosResponse>((resolve, reject) => {
        failedQueue.push({
          resolve: (token: string) => {
            if (originalRequest.headers) {
              originalRequest.headers['Authorization'] = `Bearer ${token}`;
            }
            resolve(apiClient(originalRequest));
          },
          reject,
        });
      });
    }

    originalRequest._retry = true;
    isRefreshing = true;

    try {
      const state = store.getState();
      const refreshToken = state.auth.refreshToken;

      if (!refreshToken) {
        throw new Error('No refresh token available.');
      }

      // Call the refresh endpoint directly (no interceptor recursion).
      const response = await axios.post<{
        accessToken: string;
        refreshToken: string;
        expiresAt: string;
      }>('/api/v1/auth/refresh', { refreshToken });

      if (response.status !== 200) {
        throw new Error('Refresh failed.');
      }

      const { accessToken, refreshToken: newRefreshToken, expiresAt } =
        response.data;

      // Lazily import to avoid circular dep; store is already initialised here.
      const { setCredentials } = await import('../redux/slices/authSlice');
      // Preserve the existing user object.
      const currentUser = state.auth.user;
      store.dispatch(
        setCredentials({
          accessToken,
          refreshToken: newRefreshToken,
          expiresAt,
          user: currentUser ?? { id: '', username: '', role: 'Viewer' },
        }),
      );

      processQueue(null, accessToken);

      if (originalRequest.headers) {
        originalRequest.headers['Authorization'] = `Bearer ${accessToken}`;
      }
      return apiClient(originalRequest);
    } catch (refreshError) {
      processQueue(refreshError, null);

      // Force logout — import clearCredentials action.
      const { clearCredentials } = await import('../redux/slices/authSlice');
      store.dispatch(clearCredentials());

      return Promise.reject(refreshError);
    } finally {
      isRefreshing = false;
    }
  },
);

export default apiClient;
