/**
 * CredentialProfileFormPage.tsx
 * ==============================
 * Form for creating or editing credential profiles.
 *
 * Features:
 *  - Create mode: /profiles/new
 *  - Edit mode: /profiles/:id/edit (loads existing data)
 *  - Fields: Name, AccessKeyId, SecretKey (password), Region, BucketName, KeyPrefix
 *  - Test Connection button (validates credentials before saving)
 *  - Save button (create or update)
 *  - Cancel button (navigate back)
 *
 * Uses:
 *  - useGetCredentialProfileByIdQuery (edit mode)
 *  - useCreateCredentialProfileMutation (create mode)
 *  - useUpdateCredentialProfileMutation (edit mode)
 *  - useTestCredentialProfileMutation
 */

import React, { useState, useEffect } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import {
  useGetCredentialProfileByIdQuery,
  useCreateCredentialProfileMutation,
  useUpdateCredentialProfileMutation,
  useTestCredentialProfileMutation,
  CreateCredentialProfileRequest,
  UpdateCredentialProfileRequest,
} from '../../redux/api/dataViewerApi';

interface FormData {
  name: string;
  accessKeyId: string;
  secretAccessKey: string;
  region: string;
  bucketName: string;
  keyPrefix: string;
}

const CredentialProfileFormPage: React.FC = () => {
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();
  const isEditMode = Boolean(id);

  // RTK Query hooks
  const { data: existingProfile, isLoading: isLoadingProfile } = useGetCredentialProfileByIdQuery(
    id!,
    { skip: !isEditMode }
  );
  const [createProfile, { isLoading: isCreating }] = useCreateCredentialProfileMutation();
  const [updateProfile, { isLoading: isUpdating }] = useUpdateCredentialProfileMutation();
  const [testProfile, { isLoading: isTesting }] = useTestCredentialProfileMutation();

  // Form state
  const [formData, setFormData] = useState<FormData>({
    name: '',
    accessKeyId: '',
    secretAccessKey: '',
    region: 'us-east-1',
    bucketName: '',
    keyPrefix: '',
  });

  const [errors, setErrors] = useState<Record<string, string>>({});
  const [testResult, setTestResult] = useState<{ success: boolean; message: string } | null>(null);
  const [generalError, setGeneralError] = useState<string>('');

  // Populate form in edit mode
  useEffect(() => {
    if (isEditMode && existingProfile) {
      setFormData({
        name: existingProfile.name,
        accessKeyId: existingProfile.accessKeyId,
        secretAccessKey: '', // Never pre-populate secret
        region: existingProfile.region,
        bucketName: existingProfile.bucketName,
        keyPrefix: existingProfile.keyPrefix || '',
      });
    }
  }, [isEditMode, existingProfile]);

  const handleChange = (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => {
    const { name, value } = e.target;
    setFormData((prev) => ({ ...prev, [name]: value }));
    // Clear error for this field
    setErrors((prev) => ({ ...prev, [name]: '' }));
    setGeneralError('');
  };

  const validateForm = (): boolean => {
    const newErrors: Record<string, string> = {};

    if (!formData.name.trim()) {
      newErrors.name = 'Name is required';
    }
    if (!formData.accessKeyId.trim()) {
      newErrors.accessKeyId = 'Access Key ID is required';
    }
    if (!isEditMode && !formData.secretAccessKey.trim()) {
      newErrors.secretAccessKey = 'Secret Access Key is required';
    }
    if (!formData.region.trim()) {
      newErrors.region = 'Region is required';
    }
    if (!formData.bucketName.trim()) {
      newErrors.bucketName = 'Bucket Name is required';
    }

    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleTestConnection = async () => {
    if (!validateForm()) {
      setGeneralError('Please fix validation errors before testing the connection.');
      return;
    }

    // For testing, we need to save first if creating, or use existing ID if editing
    if (isEditMode && id) {
      try {
        const result = await testProfile(id).unwrap();
        setTestResult(result);
      } catch (err: any) {
        setTestResult({
          success: false,
          message: err?.data?.detail || err?.message || 'Connection test failed',
        });
      }
    } else {
      setTestResult({
        success: false,
        message: 'Please save the profile first, then test the connection.',
      });
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setGeneralError('');
    setTestResult(null);

    if (!validateForm()) {
      return;
    }

    try {
      if (isEditMode && id) {
        // Update existing profile
        const updateData: UpdateCredentialProfileRequest = {
          name: formData.name,
          accessKeyId: formData.accessKeyId,
          region: formData.region,
          bucketName: formData.bucketName,
          keyPrefix: formData.keyPrefix || null,
        };
        // Only include secretAccessKey if provided (to rotate key)
        if (formData.secretAccessKey.trim()) {
          updateData.secretAccessKey = formData.secretAccessKey;
        }

        await updateProfile({ id, body: updateData }).unwrap();
        alert('Profile updated successfully!');
        navigate('/profiles');
      } else {
        // Create new profile
        const createData: CreateCredentialProfileRequest = {
          name: formData.name,
          accessKeyId: formData.accessKeyId,
          secretAccessKey: formData.secretAccessKey,
          region: formData.region,
          bucketName: formData.bucketName,
          keyPrefix: formData.keyPrefix || null,
        };

        await createProfile(createData).unwrap();
        alert('Profile created successfully!');
        navigate('/profiles');
      }
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

  const handleCancel = () => {
    navigate('/profiles');
  };

  if (isEditMode && isLoadingProfile) {
    return (
      <div style={styles.container}>
        <div style={styles.message}>
          <div style={styles.spinner} />
          Loading profile...
        </div>
      </div>
    );
  }

  return (
    <div style={styles.container}>
      <h1 style={styles.title}>
        {isEditMode ? 'Edit Credential Profile' : 'Create Credential Profile'}
      </h1>

      {generalError && <div style={styles.error}>{generalError}</div>}

      <form onSubmit={handleSubmit} style={styles.form}>
        <div style={styles.formGroup}>
          <label htmlFor="name" style={styles.label}>
            Profile Name <span style={styles.required}>*</span>
          </label>
          <input
            id="name"
            name="name"
            type="text"
            value={formData.name}
            onChange={handleChange}
            style={{
              ...styles.input,
              ...(errors.name ? styles.inputError : {}),
            }}
            placeholder="e.g., Production S3"
          />
          {errors.name && <div style={styles.fieldError}>{errors.name}</div>}
        </div>

        <div style={styles.formGroup}>
          <label htmlFor="accessKeyId" style={styles.label}>
            AWS Access Key ID <span style={styles.required}>*</span>
          </label>
          <input
            id="accessKeyId"
            name="accessKeyId"
            type="text"
            value={formData.accessKeyId}
            onChange={handleChange}
            style={{
              ...styles.input,
              ...(errors.accessKeyId ? styles.inputError : {}),
            }}
            placeholder="AKIAIOSFODNN7EXAMPLE"
          />
          {errors.accessKeyId && <div style={styles.fieldError}>{errors.accessKeyId}</div>}
        </div>

        <div style={styles.formGroup}>
          <label htmlFor="secretAccessKey" style={styles.label}>
            AWS Secret Access Key {!isEditMode && <span style={styles.required}>*</span>}
          </label>
          {isEditMode && (
            <div style={styles.helperText}>
              Leave blank to keep existing secret. Provide new value to rotate the key.
            </div>
          )}
          <input
            id="secretAccessKey"
            name="secretAccessKey"
            type="password"
            value={formData.secretAccessKey}
            onChange={handleChange}
            style={{
              ...styles.input,
              ...(errors.secretAccessKey ? styles.inputError : {}),
            }}
            placeholder={isEditMode ? '(hidden)' : 'wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY'}
          />
          {errors.secretAccessKey && <div style={styles.fieldError}>{errors.secretAccessKey}</div>}
        </div>

        <div style={styles.formGroup}>
          <label htmlFor="region" style={styles.label}>
            AWS Region <span style={styles.required}>*</span>
          </label>
          <select
            id="region"
            name="region"
            value={formData.region}
            onChange={handleChange}
            style={{
              ...styles.select,
              ...(errors.region ? styles.inputError : {}),
            }}
          >
            <option value="us-east-1">US East (N. Virginia) - us-east-1</option>
            <option value="us-east-2">US East (Ohio) - us-east-2</option>
            <option value="us-west-1">US West (N. California) - us-west-1</option>
            <option value="us-west-2">US West (Oregon) - us-west-2</option>
            <option value="eu-west-1">EU (Ireland) - eu-west-1</option>
            <option value="eu-west-2">EU (London) - eu-west-2</option>
            <option value="eu-central-1">EU (Frankfurt) - eu-central-1</option>
            <option value="ap-southeast-1">Asia Pacific (Singapore) - ap-southeast-1</option>
            <option value="ap-southeast-2">Asia Pacific (Sydney) - ap-southeast-2</option>
            <option value="ap-northeast-1">Asia Pacific (Tokyo) - ap-northeast-1</option>
          </select>
          {errors.region && <div style={styles.fieldError}>{errors.region}</div>}
        </div>

        <div style={styles.formGroup}>
          <label htmlFor="bucketName" style={styles.label}>
            S3 Bucket Name <span style={styles.required}>*</span>
          </label>
          <input
            id="bucketName"
            name="bucketName"
            type="text"
            value={formData.bucketName}
            onChange={handleChange}
            style={{
              ...styles.input,
              ...(errors.bucketName ? styles.inputError : {}),
            }}
            placeholder="my-transaction-logs-bucket"
          />
          {errors.bucketName && <div style={styles.fieldError}>{errors.bucketName}</div>}
        </div>

        <div style={styles.formGroup}>
          <label htmlFor="keyPrefix" style={styles.label}>
            Key Prefix (optional)
          </label>
          <div style={styles.helperText}>
            Limit searches to objects with this prefix (e.g., "logs/2024/")
          </div>
          <input
            id="keyPrefix"
            name="keyPrefix"
            type="text"
            value={formData.keyPrefix}
            onChange={handleChange}
            style={styles.input}
            placeholder="logs/"
          />
        </div>

        {testResult && (
          <div
            style={{
              ...styles.testResult,
              ...(testResult.success ? styles.testSuccess : styles.testFailure),
            }}
          >
            <strong>{testResult.success ? 'Success: ' : 'Failed: '}</strong>
            {testResult.message}
          </div>
        )}

        <div style={styles.buttonRow}>
          <button
            type="submit"
            style={styles.buttonPrimary}
            disabled={isCreating || isUpdating}
          >
            {isCreating || isUpdating
              ? 'Saving...'
              : isEditMode
              ? 'Update Profile'
              : 'Create Profile'}
          </button>
          <button
            type="button"
            onClick={handleTestConnection}
            style={styles.buttonWarning}
            disabled={isTesting}
          >
            {isTesting ? 'Testing...' : 'Test Connection'}
          </button>
          <button
            type="button"
            onClick={handleCancel}
            style={styles.buttonSecondary}
          >
            Cancel
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
    marginBottom: '1.5rem',
    color: '#2c3e50',
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
  },
  select: {
    padding: '0.625rem',
    border: '1px solid #ddd',
    borderRadius: '4px',
    fontSize: '0.95rem',
    backgroundColor: '#fff',
    fontFamily: 'inherit',
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
  testResult: {
    padding: '1rem',
    borderRadius: '4px',
    fontSize: '0.9rem',
  },
  testSuccess: {
    backgroundColor: '#d4edda',
    color: '#155724',
    border: '1px solid #c3e6cb',
  },
  testFailure: {
    backgroundColor: '#f8d7da',
    color: '#721c24',
    border: '1px solid #f5c6cb',
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
  buttonWarning: {
    padding: '0.75rem 1.5rem',
    backgroundColor: '#f39c12',
    color: '#fff',
    border: 'none',
    borderRadius: '4px',
    fontSize: '0.95rem',
    fontWeight: 500,
    cursor: 'pointer',
  },
  buttonSecondary: {
    padding: '0.75rem 1.5rem',
    backgroundColor: '#95a5a6',
    color: '#fff',
    border: 'none',
    borderRadius: '4px',
    fontSize: '0.95rem',
    fontWeight: 500,
    cursor: 'pointer',
  },
};

export default CredentialProfileFormPage;
