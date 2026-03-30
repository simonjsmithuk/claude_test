/**
 * LoginPage.tsx
 * =============
 * Login form for username/password authentication.
 * On successful login, stores tokens in Redux and localStorage, then redirects
 * to /transactions.
 *
 * Uses:
 *  - useLoginMutation from RTK Query
 *  - setCredentials action from authSlice
 */

import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAppDispatch, useAppSelector } from '../../redux/hooks';
import { useLoginMutation } from '../../redux/api/dataViewerApi';
import { setCredentials, selectIsAuthenticated } from '../../redux/slices/authSlice';

/**
 * Decodes a JWT to extract user information.
 * Returns null if the token cannot be decoded.
 */
function decodeJwt(token: string): { sub: string; name: string; role: string } | null {
  try {
    const base64Url = token.split('.')[1];
    const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
    const jsonPayload = decodeURIComponent(
      atob(base64)
        .split('')
        .map((c) => '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2))
        .join('')
    );
    const payload = JSON.parse(jsonPayload);

    // Extract role from the Microsoft schema claim name
    const roleClaim = payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'];

    return {
      sub: payload.sub,
      name: payload.name,
      role: roleClaim || 'Viewer', // Default to Viewer if role claim is missing
    };
  } catch {
    return null;
  }
}

const LoginPage: React.FC = () => {
  const navigate = useNavigate();
  const dispatch = useAppDispatch();
  const isAuthenticated = useAppSelector(selectIsAuthenticated);

  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');

  const [login, { isLoading, error }] = useLoginMutation();

  // Redirect if already authenticated
  useEffect(() => {
    if (isAuthenticated) {
      navigate('/transactions', { replace: true });
    }
  }, [isAuthenticated, navigate]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    if (!username.trim() || !password.trim()) {
      return;
    }

    try {
      const response = await login({ username, password }).unwrap();

      // Decode JWT to extract user info
      const decoded = decodeJwt(response.accessToken);
      if (!decoded) {
        console.error('Failed to decode access token');
        return;
      }

      // Store credentials in Redux and sessionStorage
      dispatch(
        setCredentials({
          accessToken: response.accessToken,
          refreshToken: response.refreshToken,
          expiresAt: response.expiresAt,
          user: {
            id: decoded.sub,
            username: decoded.name,
            role: decoded.role as 'Admin' | 'Viewer',
          },
        })
      );

      // Navigate to transactions page
      navigate('/transactions', { replace: true });
    } catch (err) {
      // Error handled by RTK Query — displayed below form
      console.error('Login failed:', err);
    }
  };

  return (
    <div style={styles.container}>
      <div style={styles.card}>
        <h1 style={styles.title}>DataViewer Login</h1>
        <form onSubmit={handleSubmit} style={styles.form}>
          <div style={styles.formGroup}>
            <label htmlFor="username" style={styles.label}>
              Username
            </label>
            <input
              id="username"
              type="text"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              disabled={isLoading}
              style={styles.input}
              autoComplete="username"
              autoFocus
            />
          </div>

          <div style={styles.formGroup}>
            <label htmlFor="password" style={styles.label}>
              Password
            </label>
            <input
              id="password"
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              disabled={isLoading}
              style={styles.input}
              autoComplete="current-password"
            />
          </div>

          {error && (
            <div style={styles.error}>
              {'status' in error && error.status === 401
                ? 'Invalid username or password'
                : 'An error occurred during login. Please try again.'}
            </div>
          )}

          <button type="submit" disabled={isLoading} style={styles.button}>
            {isLoading ? 'Logging in...' : 'Log In'}
          </button>
        </form>
      </div>
    </div>
  );
};

// ---------------------------------------------------------------------------
// Inline styles
// ---------------------------------------------------------------------------

const styles: Record<string, React.CSSProperties> = {
  container: {
    display: 'flex',
    justifyContent: 'center',
    alignItems: 'center',
    minHeight: '80vh',
  },
  card: {
    backgroundColor: '#fff',
    padding: '2rem',
    borderRadius: '8px',
    boxShadow: '0 4px 12px rgba(0,0,0,0.15)',
    width: '100%',
    maxWidth: '400px',
  },
  title: {
    fontSize: '1.75rem',
    marginBottom: '1.5rem',
    textAlign: 'center',
    color: '#2c3e50',
  },
  form: {
    display: 'flex',
    flexDirection: 'column',
    gap: '1rem',
  },
  formGroup: {
    display: 'flex',
    flexDirection: 'column',
    gap: '0.5rem',
  },
  label: {
    fontSize: '0.95rem',
    fontWeight: 500,
    color: '#555',
  },
  input: {
    padding: '0.75rem',
    border: '1px solid #ddd',
    borderRadius: '4px',
    fontSize: '1rem',
  },
  button: {
    padding: '0.75rem',
    backgroundColor: '#3498db',
    color: '#fff',
    border: 'none',
    borderRadius: '4px',
    fontSize: '1rem',
    fontWeight: 500,
    marginTop: '0.5rem',
  },
  error: {
    padding: '0.75rem',
    backgroundColor: '#fee',
    color: '#c33',
    borderRadius: '4px',
    fontSize: '0.9rem',
    border: '1px solid #fcc',
  },
};

export default LoginPage;
