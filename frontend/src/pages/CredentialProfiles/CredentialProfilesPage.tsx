/**
 * CredentialProfilesPage.tsx
 * ===========================
 * List all credential profiles with actions for create, edit, delete,
 * test connection, and activate.
 *
 * Features:
 *  - Table with columns: Name, AWS Region, Bucket Name, Key Prefix, IsActive
 *  - Actions per row: Edit, Delete, Test Connection, Activate
 *  - "Add New" button to navigate to form page
 *  - Loading spinner and empty state handling
 *
 * Uses:
 *  - useGetCredentialProfilesQuery from RTK Query
 *  - useDeleteCredentialProfileMutation
 *  - useTestCredentialProfileMutation
 *  - useActivateCredentialProfileMutation
 */

import React, { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import {
  useGetCredentialProfilesQuery,
  useDeleteCredentialProfileMutation,
  useTestCredentialProfileMutation,
  useActivateCredentialProfileMutation,
  CredentialProfileDto,
} from '../../redux/api/dataViewerApi';

const CredentialProfilesPage: React.FC = () => {
  const navigate = useNavigate();
  const { data, error, isLoading, isFetching } = useGetCredentialProfilesQuery();
  const [deleteProfile] = useDeleteCredentialProfileMutation();
  const [testProfile, { isLoading: isTesting }] = useTestCredentialProfileMutation();
  const [activateProfile, { isLoading: isActivating }] = useActivateCredentialProfileMutation();

  const [testResults, setTestResults] = useState<Record<string, { success: boolean; message: string }>>({});
  const [deletingId, setDeletingId] = useState<string | null>(null);
  const [activatingId, setActivatingId] = useState<string | null>(null);

  const handleDelete = async (id: string, name: string) => {
    if (!window.confirm(`Are you sure you want to delete profile "${name}"?`)) {
      return;
    }

    setDeletingId(id);
    try {
      await deleteProfile(id).unwrap();
      alert(`Profile "${name}" deleted successfully.`);
    } catch (err: any) {
      alert(`Failed to delete profile: ${err?.data?.detail || err?.message || 'Unknown error'}`);
    } finally {
      setDeletingId(null);
    }
  };

  const handleTest = async (id: string) => {
    try {
      const result = await testProfile(id).unwrap();
      setTestResults((prev) => ({ ...prev, [id]: result }));
    } catch (err: any) {
      setTestResults((prev) => ({
        ...prev,
        [id]: {
          success: false,
          message: err?.data?.detail || err?.message || 'Connection test failed',
        },
      }));
    }
  };

  const handleActivate = async (id: string, name: string) => {
    if (!window.confirm(`Activate profile "${name}"? This will deactivate all other profiles.`)) {
      return;
    }

    setActivatingId(id);
    try {
      await activateProfile(id).unwrap();
      alert(`Profile "${name}" is now active.`);
      setActivatingId(null);
    } catch (err: any) {
      alert(`Failed to activate profile: ${err?.data?.detail || err?.message || 'Unknown error'}`);
      setActivatingId(null);
    }
  };

  return (
    <div style={styles.container}>
      <div style={styles.header}>
        <h1 style={styles.title}>Credential Profiles</h1>
        <button
          style={styles.buttonPrimary}
          onClick={() => navigate('/profiles/new')}
        >
          Add New Profile
        </button>
      </div>

      {/* Loading state */}
      {isLoading && (
        <div style={styles.message}>
          <div style={styles.spinner} />
          Loading credential profiles...
        </div>
      )}

      {/* Error state */}
      {error && (
        <div style={styles.error}>
          Failed to load credential profiles. Please try again.
        </div>
      )}

      {/* Results */}
      {data && (
        <>
          <div style={styles.resultInfo}>
            {data.totalCount} profile{data.totalCount !== 1 ? 's' : ''} found
            {isFetching && <span style={styles.fetching}> (updating...)</span>}
          </div>

          {data.data.length === 0 ? (
            <div style={styles.emptyState}>
              <p>No credential profiles configured.</p>
              <p>Click "Add New Profile" to create your first profile.</p>
            </div>
          ) : (
            <div style={styles.tableContainer}>
              <table style={styles.table}>
                <thead>
                  <tr style={styles.headerRow}>
                    <th style={styles.th}>Name</th>
                    <th style={styles.th}>AWS Region</th>
                    <th style={styles.th}>Bucket Name</th>
                    <th style={styles.th}>Key Prefix</th>
                    <th style={styles.th}>Status</th>
                    <th style={styles.th}>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {data.data
                    .filter((profile) => !profile.isDeleted)
                    .map((profile) => {
                      const testResult = testResults[profile.id];
                      return (
                        <tr key={profile.id} style={styles.row}>
                          <td style={styles.td}>
                            <strong>{profile.name}</strong>
                          </td>
                          <td style={styles.td}>{profile.region}</td>
                          <td style={styles.td}>
                            <span style={styles.monospace}>{profile.bucketName}</span>
                          </td>
                          <td style={styles.td}>
                            <span style={styles.monospace}>
                              {profile.keyPrefix || <em>(none)</em>}
                            </span>
                          </td>
                          <td style={styles.td}>
                            <span
                              style={{
                                ...styles.statusBadge,
                                ...(profile.isActive ? styles.statusActive : styles.statusInactive),
                              }}
                            >
                              {profile.isActive ? 'Active' : 'Inactive'}
                            </span>
                          </td>
                          <td style={styles.td}>
                            <div style={styles.actionButtons}>
                              <button
                                style={styles.buttonSmall}
                                onClick={() => navigate(`/profiles/${profile.id}/edit`)}
                              >
                                Edit
                              </button>
                              <button
                                style={styles.buttonSmallWarning}
                                onClick={() => handleTest(profile.id)}
                                disabled={isTesting}
                              >
                                {isTesting ? 'Testing...' : 'Test'}
                              </button>
                              {!profile.isActive && (
                                <button
                                  style={styles.buttonSmallSuccess}
                                  onClick={() => handleActivate(profile.id, profile.name)}
                                  disabled={isActivating || activatingId === profile.id}
                                >
                                  {activatingId === profile.id ? 'Activating...' : 'Activate'}
                                </button>
                              )}
                              <button
                                style={styles.buttonSmallDanger}
                                onClick={() => handleDelete(profile.id, profile.name)}
                                disabled={deletingId === profile.id}
                              >
                                {deletingId === profile.id ? 'Deleting...' : 'Delete'}
                              </button>
                            </div>
                            {testResult && (
                              <div
                                style={{
                                  ...styles.testResult,
                                  ...(testResult.success
                                    ? styles.testSuccess
                                    : styles.testFailure),
                                }}
                              >
                                {testResult.message}
                              </div>
                            )}
                          </td>
                        </tr>
                      );
                    })}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}
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
  },
  header: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: '1.5rem',
  },
  title: {
    fontSize: '1.75rem',
    color: '#2c3e50',
    margin: 0,
  },
  buttonPrimary: {
    padding: '0.625rem 1.5rem',
    backgroundColor: '#3498db',
    color: '#fff',
    border: 'none',
    borderRadius: '4px',
    fontSize: '0.95rem',
    fontWeight: 500,
    cursor: 'pointer',
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
  resultInfo: {
    marginBottom: '1rem',
    fontSize: '0.95rem',
    color: '#666',
  },
  fetching: {
    fontStyle: 'italic',
    color: '#999',
  },
  emptyState: {
    padding: '3rem',
    textAlign: 'center',
    color: '#999',
    backgroundColor: '#f8f9fa',
    borderRadius: '6px',
  },
  tableContainer: {
    overflowX: 'auto',
    marginBottom: '1.5rem',
  },
  table: {
    width: '100%',
    borderCollapse: 'collapse',
  },
  headerRow: {
    backgroundColor: '#ecf0f1',
    borderBottom: '2px solid #bdc3c7',
  },
  th: {
    padding: '0.75rem',
    textAlign: 'left',
    fontWeight: 600,
    fontSize: '0.9rem',
    color: '#2c3e50',
  },
  row: {
    borderBottom: '1px solid #ecf0f1',
  },
  td: {
    padding: '0.75rem',
    fontSize: '0.9rem',
    color: '#555',
    verticalAlign: 'top',
  },
  monospace: {
    fontFamily: 'monospace',
    fontSize: '0.85rem',
    backgroundColor: '#f8f9fa',
    padding: '0.2rem 0.4rem',
    borderRadius: '3px',
  },
  statusBadge: {
    display: 'inline-block',
    padding: '0.25rem 0.6rem',
    borderRadius: '12px',
    fontSize: '0.8rem',
    fontWeight: 500,
  },
  statusActive: {
    backgroundColor: '#2ecc71',
    color: '#fff',
  },
  statusInactive: {
    backgroundColor: '#95a5a6',
    color: '#fff',
  },
  actionButtons: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: '0.5rem',
    marginBottom: '0.5rem',
  },
  buttonSmall: {
    padding: '0.375rem 0.75rem',
    backgroundColor: '#3498db',
    color: '#fff',
    border: 'none',
    borderRadius: '4px',
    fontSize: '0.85rem',
    cursor: 'pointer',
  },
  buttonSmallWarning: {
    padding: '0.375rem 0.75rem',
    backgroundColor: '#f39c12',
    color: '#fff',
    border: 'none',
    borderRadius: '4px',
    fontSize: '0.85rem',
    cursor: 'pointer',
  },
  buttonSmallSuccess: {
    padding: '0.375rem 0.75rem',
    backgroundColor: '#27ae60',
    color: '#fff',
    border: 'none',
    borderRadius: '4px',
    fontSize: '0.85rem',
    cursor: 'pointer',
  },
  buttonSmallDanger: {
    padding: '0.375rem 0.75rem',
    backgroundColor: '#e74c3c',
    color: '#fff',
    border: 'none',
    borderRadius: '4px',
    fontSize: '0.85rem',
    cursor: 'pointer',
  },
  testResult: {
    marginTop: '0.5rem',
    padding: '0.5rem',
    borderRadius: '4px',
    fontSize: '0.8rem',
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
};

export default CredentialProfilesPage;
