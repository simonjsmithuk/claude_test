/**
 * UserPreferencesPage.tsx
 * ========================
 * User preferences page for viewing and editing personal UI preferences.
 *
 * Features:
 *  - Load current preferences on mount using GET /api/v1/admin/preferences
 *  - Form to edit user preferences with validation
 *  - Save Preferences button to PUT /api/v1/admin/preferences
 *  - Loading states during fetch and save operations
 *  - Success message after save
 *  - Error handling with detailed error messages
 *
 * Preferences fields:
 *  - defaultPageSize (1-200) - number of records per page in search results
 *  - defaultDateRangeDays (1-365) - default date range filter in days
 *  - preferredProfileId (Guid or null) - preferred credential profile for searches
 *
 * Uses:
 *  - useGetUserPreferencesQuery
 *  - useUpdateUserPreferencesMutation
 *  - useGetCredentialProfilesQuery (for profile dropdown)
 */

import React, { useState, useEffect } from 'react';
import {
  useGetUserPreferencesQuery,
  useUpdateUserPreferencesMutation,
  useGetCredentialProfilesQuery,
  UserPreferenceDto,
} from '../../redux/api/dataViewerApi';

interface FormData {
  defaultPageSize: number;
  defaultDateRangeDays: number;
  preferredProfileId: string;
}

const UserPreferencesPage: React.FC = () => {
  // RTK Query hooks
  const { data: preferences, isLoading, error: fetchError } = useGetUserPreferencesQuery();
  const { data: profilesData, isLoading: isLoadingProfiles } = useGetCredentialProfilesQuery();
  const [updatePreferences, { isLoading: isSaving }] = useUpdateUserPreferencesMutation();

  // Form state
  const [formData, setFormData] = useState<FormData>({
    defaultPageSize: 25,
    defaultDateRangeDays: 7,
    preferredProfileId: '',
  });

  const [errors, setErrors] = useState<Record<string, string>>({});
  const [generalError, setGeneralError] = useState<string>('');
  const [successMessage, setSuccessMessage] = useState<string>('');

  // Populate form when preferences load
  useEffect(() => {
    if (preferences) {
      setFormData({
        defaultPageSize: preferences.defaultPageSize,
        defaultDateRangeDays: preferences.defaultDateRangeDays,
        preferredProfileId: preferences.preferredProfileId || '',
      });
    }
  }, [preferences]);

  const handleChange = (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => {
    const { name, value } = e.target;

    // Parse as number for number inputs
    let parsedValue: string | number = value;
    if (name === 'defaultPageSize' || name === 'defaultDateRangeDays') {
      const numValue = parseInt(value, 10);
      parsedValue = isNaN(numValue) ? 0 : numValue;
    }

    setFormData((prev) => ({ ...prev, [name]: parsedValue }));
    // Clear error for this field
    setErrors((prev) => ({ ...prev, [name]: '' }));
    setGeneralError('');
    setSuccessMessage('');
  };

  const validateForm = (): boolean => {
    const newErrors: Record<string, string> = {};

    if (formData.defaultPageSize < 1 || formData.defaultPageSize > 200) {
      newErrors.defaultPageSize = 'Must be between 1 and 200';
    }
    if (formData.defaultDateRangeDays < 1 || formData.defaultDateRangeDays > 365) {
      newErrors.defaultDateRangeDays = 'Must be between 1 and 365 days';
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
      const updateData: UserPreferenceDto = {
        defaultPageSize: formData.defaultPageSize,
        defaultDateRangeDays: formData.defaultDateRangeDays,
        preferredProfileId: formData.preferredProfileId || null,
      };

      await updatePreferences(updateData).unwrap();
      setSuccessMessage('Preferences saved successfully!');
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
  if (isLoading || isLoadingProfiles) {
    return (
      <div style={styles.container}>
        <div style={styles.message}>
          <div style={styles.spinner} />
          Loading preferences...
        </div>
      </div>
    );
  }

  // Fetch error state
  if (fetchError) {
    const errMsg = (fetchError as any)?.data?.detail || 'Failed to load preferences';
    return (
      <div style={styles.container}>
        <h1 style={styles.title}>User Preferences</h1>
        <div style={styles.error}>{errMsg}</div>
      </div>
    );
  }

  // Get active (non-deleted) profiles for dropdown
  const activeProfiles = profilesData?.data.filter(p => !p.isDeleted) || [];

  return (
    <div style={styles.container}>
      <h1 style={styles.title}>User Preferences</h1>
      <p style={styles.subtitle}>
        Configure your personal UI preferences. These settings control default values
        in the transaction search page.
      </p>

      {generalError && <div style={styles.error}>{generalError}</div>}
      {successMessage && <div style={styles.success}>{successMessage}</div>}

      <form onSubmit={handleSubmit} style={styles.form}>
        <div style={styles.formGroup}>
          <label htmlFor="defaultPageSize" style={styles.label}>
            Default Page Size <span style={styles.required}>*</span>
          </label>
          <div style={styles.helperText}>
            Number of transaction records to display per page in search results.
            Range: 1-200 records per page.
          </div>
          <input
            id="defaultPageSize"
            name="defaultPageSize"
            type="number"
            min={1}
            max={200}
            step={1}
            value={formData.defaultPageSize}
            onChange={handleChange}
            style={{
              ...styles.input,
              ...(errors.defaultPageSize ? styles.inputError : {}),
            }}
          />
          {errors.defaultPageSize && (
            <div style={styles.fieldError}>{errors.defaultPageSize}</div>
          )}
        </div>

        <div style={styles.formGroup}>
          <label htmlFor="defaultDateRangeDays" style={styles.label}>
            Default Date Range (days) <span style={styles.required}>*</span>
          </label>
          <div style={styles.helperText}>
            Default look-back window in calendar days for the date range filter when
            opening the transaction search page. Range: 1-365 days.
          </div>
          <input
            id="defaultDateRangeDays"
            name="defaultDateRangeDays"
            type="number"
            min={1}
            max={365}
            step={1}
            value={formData.defaultDateRangeDays}
            onChange={handleChange}
            style={{
              ...styles.input,
              ...(errors.defaultDateRangeDays ? styles.inputError : {}),
            }}
          />
          {errors.defaultDateRangeDays && (
            <div style={styles.fieldError}>{errors.defaultDateRangeDays}</div>
          )}
        </div>

        <div style={styles.formGroup}>
          <label htmlFor="preferredProfileId" style={styles.label}>
            Preferred Credential Profile
          </label>
          <div style={styles.helperText}>
            The credential profile pre-selected in the profile picker when you open
            the transaction search page. Leave empty to use the system-active profile.
          </div>
          <select
            id="preferredProfileId"
            name="preferredProfileId"
            value={formData.preferredProfileId}
            onChange={handleChange}
            style={{
              ...styles.select,
              ...(errors.preferredProfileId ? styles.inputError : {}),
            }}
          >
            <option value="">Use system-active profile</option>
            {activeProfiles.map((profile) => (
              <option key={profile.id} value={profile.id}>
                {profile.name} ({profile.region} - {profile.bucketName})
                {profile.isActive ? ' [ACTIVE]' : ''}
              </option>
            ))}
          </select>
          {errors.preferredProfileId && (
            <div style={styles.fieldError}>{errors.preferredProfileId}</div>
          )}
        </div>

        <div style={styles.buttonRow}>
          <button type="submit" style={styles.buttonPrimary} disabled={isSaving}>
            {isSaving ? 'Saving...' : 'Save Preferences'}
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
  select: {
    padding: '0.625rem',
    border: '1px solid #ddd',
    borderRadius: '4px',
    fontSize: '0.95rem',
    fontFamily: 'inherit',
    width: '100%',
    maxWidth: '500px',
    backgroundColor: '#fff',
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

export default UserPreferencesPage;
