/**
 * PrivateRoute.tsx
 * ================
 * Protected route wrapper that checks authentication state from Redux.
 * Redirects to /login if not authenticated.
 * Optionally checks for Admin role if requireAdmin prop is true.
 */

import React from 'react';
import { Navigate } from 'react-router-dom';
import { useAppSelector } from '../../redux/hooks';
import { selectIsAuthenticated, selectCurrentUser } from '../../redux/slices/authSlice';

interface PrivateRouteProps {
  children: React.ReactNode;
  requireAdmin?: boolean;
}

const PrivateRoute: React.FC<PrivateRouteProps> = ({ children, requireAdmin = false }) => {
  const isAuthenticated = useAppSelector(selectIsAuthenticated);
  const user = useAppSelector(selectCurrentUser);

  // Not authenticated — redirect to login
  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }

  // Authenticated but insufficient privileges
  if (requireAdmin && user?.role !== 'Admin') {
    return (
      <div style={styles.accessDenied}>
        <h1>Access Denied</h1>
        <p>You do not have permission to view this page.</p>
        <p>Admin role required.</p>
      </div>
    );
  }

  // All checks passed — render children
  return <>{children}</>;
};

// ---------------------------------------------------------------------------
// Inline styles
// ---------------------------------------------------------------------------

const styles: Record<string, React.CSSProperties> = {
  accessDenied: {
    backgroundColor: '#fff',
    padding: '2rem',
    borderRadius: '8px',
    boxShadow: '0 2px 8px rgba(0,0,0,0.1)',
    textAlign: 'center',
  },
};

export default PrivateRoute;
