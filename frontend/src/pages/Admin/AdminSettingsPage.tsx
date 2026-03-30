/**
 * AdminSettingsPage.tsx
 * ======================
 * Admin-only page for viewing and editing system-wide settings.
 *
 * Features:
 *  - Load current settings on mount using GET /api/v1/admin/settings
 *  - Form to edit system settings with validation
 *  - Save Changes button to PUT /api/v1/admin/settings
 *  - Loading states during fetch and save operations
 *  - Success message after save
 *  - Error handling with detailed error messages
 *
 * Settings fields (all number inputs with validation):
 *  - jwtAccessTokenMinutes (5-120)
 *  - jwtRefreshTokenHours (1-720)
 *  - bodySizeCapMb (1-1000)
 *  - lockoutThreshold (0-10, 0 disables lockout)
 *
 * Uses:
 *  - useGetAdminSettingsQuery
 *  - useUpdateAdminSettingsMutation
 */

import React, { useState, useEffect } from 'react';
import {
  useGetAdminSettingsQuery,
  useUpdateAdminSettingsMutation,
  UpdateAdminSettingsRequest,
} from '../../redux/api/dataViewerApi';

interface FormData {
  jwtAccessTokenMinutes: number;
  jwtRefreshTokenHours: number;
  bodySizeCapMb: number;
  lockoutThreshold: number;
}

const AdminSettingsPage: React.FC = () => {
  // RTK Query hooks
  const { data: settings, isLoading, error: fetchError } = useGetAdminSettingsQuery();
  const [updateSettings, { isLoading: isSaving }] = useUpdateAdminSettingsMutation();

  // Form state
  const [formData, setFormData] = useState<FormData>({
    jwtAccessTokenMinutes: 15,
    jwtRefreshTokenHours: 24,
    bodySizeCapMb: 10,
    lockoutThreshold: 5,
  });

  const [errors, setErrors] = useState<Record<string, string>>({});
  const [generalError, setGeneralError] = useState<string>('');
  const [successMessage, setSuccessMessage] = useState<string>('');

  // Populate form when settings load
  useEffect(() => {
    if (settings) {
      setFormData({
        jwtAccessTokenMinutes: settings.jwtAccessTokenMinutes,
        jwtRefreshTokenHours: settings.jwtRefreshTokenHours,
        bodySizeCapMb: settings.bodySizeCapMb,
        lockoutThreshold: settings.lockoutThreshold,
      });
    }
  }, [settings]);

  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const { name, value } = e.target;
    // Parse as number for number inputs
    const numValue = parseInt(value, 10);
    setFormData((prev) => ({ ...prev, [name]: isNaN(numValue) ? 0 : numValue }));
    // Clear error for this field
    setErrors((prev) => ({ ...prev, [name]: '' }));
    setGeneralError('');
    setSuccessMessage('');
  };

  const validateForm = (): boolean => {
    const newErrors: Record<string, string> = {};

    if (formData.jwtAccessTokenMinutes < 5 || formData.jwtAccessTokenMinutes > 120) {
      newErrors.jwtAccessTokenMinutes = 'Must be between 5 and 120 minutes';
    }
    if (formData.jwtRefreshTokenHours < 1 || formData.jwtRefreshTokenHours > 720) {
      newErrors.jwtRefreshTokenHours = 'Must be between 1 and 720 hours (30 days)';
    }
    if (formData.bodySizeCapMb < 1 || formData.bodySizeCapMb > 1000) {
      newErrors.bodySizeCapMb = 'Must be between 1 and 1000 MB';
    }
    if (formData.lockoutThreshold < 0 || formData.lockoutThreshold > 10) {
      newErrors.lockoutThreshold = 'Must be between 0 and 10 (0 disables lockout)';
    }

    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setGeneralError('');
    setSuccessMessage('');

    if (!validateForm()) {
      return;
    }

    try {
      const updateData: UpdateAdminSettingsRequest = {
        jwtAccessTokenMinutes: formData.jwtAccessTokenMinutes,
        jwtRefreshTokenHours: formData.jwtRefreshTokenHours,
        bodySizeCapMb: formData.bodySizeCapMb,
        lockoutThreshold: formData.lockoutThreshold,
      };

      await updateSettings(updateData).unwrap();
      setSuccessMessage('Settings updated successfully!');
    } catch (err: any) {
      const errorDetail = err?.data?.detail || err?.message || 'An error occurred';
      setGeneralError(errorDetail);

      // Extract field-specific errors if available
      if (err?.data?.errors) {
        const fieldErrors: Record<string, string> = {};
        Object.entries(err.data.errors).forEach(([field, messages]) => {
          fieldErrors[field] = Array.isArray(messages) ? messages.join(', ') : String(messages);
        });
        setErrors(fieldErrors);
      }
    }
  };

  // Loading state
  if (isLoading) {
    return (
      <div style={styles.container}>
        <div style={styles.message}>
          <div style={styles.spinner} />
          Loading settings...
        </div>
      </div>
    );
  }

  // Fetch error state
  if (fetchError) {
    const errMsg = (fetchError as any)?.data?.detail || 'Failed to load settings';
    return (
      <div style={styles.container}>
        <h1 style={styles.title}>System Settings</h1>
        <div style={styles.error}>{errMsg}</div>
      </div>
    );
  }

  return (
    <div style={styles.container}>
      <h1 style={styles.title}>System Settings</h1>
      <p style={styles.subtitle}>
        Configure system-wide settings. Changes take effect immediately.
      </p>

      {generalError && <div style={styles.error}>{generalError}</div>}
      {successMessage && <div style={styles.success}>{successMessage}</div>}

      <form onSubmit={handleSubmit} style={styles.form}>
        <div style={styles.formGroup}>
          <label htmlFor="jwtAccessTokenMinutes" style={styles.label}>
            Access Token Lifetime (minutes) <span style={styles.required}>*</span>
          </label>
          <div style={styles.helperText}>
            JWT access token lifetime. Shorter values improve security at the cost of more
            frequent token refreshes. Range: 5-120 minutes.
          </div>
          <input
            id="jwtAccessTokenMinutes"
            name="jwtAccessTokenMinutes"
            type="number"
            min={5}
            max={120}
            value={formData.jwtAccessTokenMinutes}
            onChange={handleChange}
            style={{
              ...styles.input,
              ...(errors.jwtAccessTokenMinutes ? styles.inputError : {}),
            }}
          />
          {errors.jwtAccessTokenMinutes && (
            <div style={styles.fieldError}>{errors.jwtAccessTokenMinutes}</div>
          )}
        </div>

        <div style={styles.formGroup}>
          <label htmlFor="jwtRefreshTokenHours" style={styles.label}>
            Refresh Token Lifetime (hours) <span style={styles.required}>*</span>
          </label>
          <div style={styles.helperText}>
            Refresh token lifetime. Refresh tokens are stored as hashes and revoked on logout.
            Range: 1-720 hours (1 hour to 30 days).
          </div>
          <input
            id="jwtRefreshTokenHours"
            name="jwtRefreshTokenHours"
            type="number"
            min={1}
            max={720}
            value={formData.jwtRefreshTokenHours}
            onChange={handleChange}
            style={{
              ...styles.input,
              ...(errors.jwtRefreshTokenHours ? styles.inputError : {}),
            }}
          />
          {errors.jwtRefreshTokenHours && (
            <div style={styles.fieldError}>{errors.jwtRefreshTokenHours}</div>
          )}
        </div>

        <div style={styles.formGroup}>
          <label htmlFor="bodySizeCapMb" style={styles.label}>
            Body Size Cap (MB) <span style={styles.required}>*</span>
          </label>
          <div style={styles.helperText}>
            Maximum size of an S3 object body that the API will decompress and return.
            Requests exceeding this cap are rejected with HTTP 413. Range: 1-1000 MB.
          </div>
          <input
            id="bodySizeCapMb"
            name="bodySizeCapMb"
            type="number"
            min={1}
            max={1000}
            value={formData.bodySizeCapMb}
            onChange={handleChange}
            style={{
              ...styles.input,
              ...(errors.bodySizeCapMb ? styles.inputError : {}),
            }}
          />
          {errors.bodySizeCapMb && (
            <div style={styles.fieldError}>{errors.bodySizeCapMb}</div>
          )}
        </div>

        <div style={styles.formGroup}>
          <label htmlFor="lockoutThreshold" style={styles.label}>
            Failed Login Lockout Threshold <span style={styles.required}>*</span>
          </label>
          <div style={styles.helperText}>
            Number of consecutive failed login attempts before a user account is automatically
            locked. Set to 0 to disable automatic lockout. Range: 0-10.
          </div>
          <input
            id="lockoutThreshold"
            name="lockoutThreshold"
            type="number"
            min={0}
            max={10}
            value={formData.lockoutThreshold}
            onChange={handleChange}
            style={{
              ...styles.input,
              ...(errors.lockoutThreshold ? styles.inputError : {}),
            }}
          />
          {errors.lockoutThreshold && (
            <div style={styles.fieldError}>{errors.lockoutThreshold}</div>
          )}
        </div>

        <div style={styles.buttonRow}>
          <button type="submit" style={styles.buttonPrimary} disabled={isSaving}>
            {isSaving ? 'Saving...' : 'Save Changes'}
          </button>
        </div>
      </form>
    </div>
  );
};

// ---------------------------------------------------------------------------
// Inline styles
// ---------------------------------------------------------------------------

const styles: Record<string, React.CSSProperties> = {
  container: {
    backgroundColor: '#fff',
    padding: '2rem',
    borderRadius: '8px',
    boxShadow: '0 2px 8px rgba(0,0,0,0.1)',
    maxWidth: '800px',
    margin: '0 auto',
  },
  title: {
    fontSize: '1.75rem',
    marginBottom: '0.5rem',
    color: '#2c3e50',
  },
  subtitle: {
    fontSize: '0.95rem',
    color: '#7f8c8d',
    marginBottom: '1.5rem',
  },
  message: {
    padding: '2rem',
    textAlign: 'center',
    color: '#666',
    fontSize: '1rem',
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: '1rem',
  },
  spinner: {
    width: '40px',
    height: '40px',
    border: '4px solid #f3f3f3',
    borderTop: '4px solid #3498db',
    borderRadius: '50%',
    animation: 'spin 1s linear infinite',
  },
  error: {
    padding: '1rem',
    backgroundColor: '#fee',
    color: '#c33',
    borderRadius: '4px',
    marginBottom: '1rem',
    border: '1px solid #fcc',
  },
  success: {
    padding: '1rem',
    backgroundColor: '#d4edda',
    color: '#155724',
    borderRadius: '4px',
    marginBottom: '1rem',
    border: '1px solid #c3e6cb',
  },
  form: {
    display: 'flex',
    flexDirection: 'column',
    gap: '1.5rem',
  },
  formGroup: {
    display: 'flex',
    flexDirection: 'column',
    gap: '0.5rem',
  },
  label: {
    fontSize: '0.95rem',
    fontWeight: 600,
    color: '#2c3e50',
  },
  required: {
    color: '#e74c3c',
  },
  helperText: {
    fontSize: '0.85rem',
    color: '#7f8c8d',
    fontStyle: 'italic',
  },
  input: {
    padding: '0.625rem',
    border: '1px solid #ddd',
    borderRadius: '4px',
    fontSize: '0.95rem',
    fontFamily: 'inherit',
    width: '200px',
  },
  inputError: {
    borderColor: '#e74c3c',
    backgroundColor: '#fff5f5',
  },
  fieldError: {
    color: '#e74c3c',
    fontSize: '0.85rem',
    marginTop: '-0.25rem',
  },
  buttonRow: {
    display: 'flex',
    gap: '0.75rem',
    marginTop: '1rem',
  },
  buttonPrimary: {
    padding: '0.75rem 1.5rem',
    backgroundColor: '#3498db',
    color: '#fff',
    border: 'none',
    borderRadius: '4px',
    fontSize: '0.95rem',
    fontWeight: 500,
    cursor: 'pointer',
  },
};

export default AdminSettingsPage;
