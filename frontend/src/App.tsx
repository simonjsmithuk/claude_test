/**
 * App.tsx
 * =======
 * Main application component with routing and navigation.
 *
 * Routes:
 *  - /login                    — Login page (public)
 *  - /transactions             — Transaction search (protected)
 *  - /transactions/:s3KeyBase64 — Transaction detail (protected)
 *  - /profiles                 — Credential profiles (protected, Admin only)
 *  - /preferences              — User preferences (protected)
 *  - /admin/settings           — System settings (protected, Admin only)
 *
 * The navigation bar appears only when authenticated.
 */

import React from 'react';
import { Routes, Route, Navigate, Link } from 'react-router-dom';
import { useAppSelector } from './redux/hooks';
import { selectIsAuthenticated, selectCurrentUser } from './redux/slices/authSlice';
import PrivateRoute from './components/Layout/PrivateRoute';
import LoginPage from './pages/Login/LoginPage';
import TransactionListPage from './pages/Transactions/TransactionListPage';
import TransactionDetailPage from './pages/Transactions/TransactionDetailPage';
import CredentialProfilesPage from './pages/CredentialProfiles/CredentialProfilesPage';
import CredentialProfileFormPage from './pages/CredentialProfiles/CredentialProfileFormPage';
import AdminSettingsPage from './pages/Admin/AdminSettingsPage';
import UserPreferencesPage from './pages/Preferences/UserPreferencesPage';

const App: React.FC = () => {
  const isAuthenticated = useAppSelector(selectIsAuthenticated);
  const user = useAppSelector(selectCurrentUser);

  return (
    <div style={styles.app}>
      {/* Navigation bar — shown only when authenticated */}
      {isAuthenticated && (
        <nav style={styles.nav}>
          <div style={styles.navContent}>
            <div style={styles.navLeft}>
              <Link to="/transactions" style={styles.navLink}>
                <strong>DataViewer</strong>
              </Link>
              <Link to="/transactions" style={styles.navLink}>
                Transactions
              </Link>
              {user?.role === 'Admin' && (
                <>
                  <Link to="/profiles" style={styles.navLink}>
                    Profiles
                  </Link>
                  <Link to="/admin/settings" style={styles.navLink}>
                    Settings
                  </Link>
                </>
              )}
              <Link to="/preferences" style={styles.navLink}>
                Preferences
              </Link>
            </div>
            <div style={styles.navRight}>
              <span style={styles.username}>
                {user?.username} ({user?.role})
              </span>
              <Link to="/login" style={styles.navLink}>
                Logout
              </Link>
            </div>
          </div>
        </nav>
      )}

      {/* Main content area */}
      <main style={styles.main}>
        <Routes>
          {/* Public route */}
          <Route path="/login" element={<LoginPage />} />

          {/* Protected routes */}
          <Route
            path="/transactions"
            element={
              <PrivateRoute>
                <TransactionListPage />
              </PrivateRoute>
            }
          />
          <Route
            path="/transactions/:s3KeyBase64"
            element={
              <PrivateRoute>
                <TransactionDetailPage />
              </PrivateRoute>
            }
          />
          <Route
            path="/profiles"
            element={
              <PrivateRoute requireAdmin>
                <CredentialProfilesPage />
              </PrivateRoute>
            }
          />
          <Route
            path="/profiles/new"
            element={
              <PrivateRoute requireAdmin>
                <CredentialProfileFormPage />
              </PrivateRoute>
            }
          />
          <Route
            path="/profiles/:id/edit"
            element={
              <PrivateRoute requireAdmin>
                <CredentialProfileFormPage />
              </PrivateRoute>
            }
          />
          <Route
            path="/preferences"
            element={
              <PrivateRoute>
                <UserPreferencesPage />
              </PrivateRoute>
            }
          />
          <Route
            path="/admin/settings"
            element={
              <PrivateRoute requireAdmin>
                <AdminSettingsPage />
              </PrivateRoute>
            }
          />

          {/* Default redirect */}
          <Route
            path="/"
            element={
              <Navigate to={isAuthenticated ? '/transactions' : '/login'} replace />
            }
          />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </main>
    </div>
  );
};

// ---------------------------------------------------------------------------
// Inline styles
// ---------------------------------------------------------------------------

const styles: Record<string, React.CSSProperties> = {
  app: {
    minHeight: '100vh',
    display: 'flex',
    flexDirection: 'column',
  },
  nav: {
    backgroundColor: '#2c3e50',
    color: '#fff',
    padding: '0 1rem',
    boxShadow: '0 2px 4px rgba(0,0,0,0.1)',
  },
  navContent: {
    maxWidth: '1400px',
    margin: '0 auto',
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    height: '60px',
  },
  navLeft: {
    display: 'flex',
    gap: '1.5rem',
    alignItems: 'center',
  },
  navRight: {
    display: 'flex',
    gap: '1rem',
    alignItems: 'center',
  },
  navLink: {
    color: '#fff',
    textDecoration: 'none',
    padding: '0.5rem 0',
    borderBottom: '2px solid transparent',
    transition: 'border-color 0.2s',
  },
  username: {
    fontSize: '0.9rem',
    color: '#ecf0f1',
  },
  main: {
    flex: 1,
    maxWidth: '1400px',
    width: '100%',
    margin: '2rem auto',
    padding: '0 1rem',
  },
  placeholder: {
    backgroundColor: '#fff',
    padding: '2rem',
    borderRadius: '8px',
    boxShadow: '0 2px 8px rgba(0,0,0,0.1)',
  },
};

export default App;
