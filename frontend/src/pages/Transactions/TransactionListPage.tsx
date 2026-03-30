/**
 * TransactionListPage.tsx
 * =======================
 * Transaction search and list page with filters and pagination.
 *
 * Features:
 *  - Search filters: status, method, date range, page
 *  - Paginated table of results
 *  - Link to detail page for each transaction
 *
 * Uses:
 *  - useSearchTransactionsQuery hook from RTK Query
 */

import React, { useState } from 'react';
import { Link } from 'react-router-dom';
import { useSearchTransactionsQuery } from '../../redux/api/dataViewerApi';

const TransactionListPage: React.FC = () => {
  // Search filter state
  const [statusCode, setStatusCode] = useState('');
  const [method, setMethod] = useState('');
  const [fromDate, setFromDate] = useState('');
  const [toDate, setToDate] = useState('');
  const [page, setPage] = useState(1);
  const pageSize = 20;

  // Build query params
  const params = {
    statusCode: statusCode || undefined,
    method: method || undefined,
    fromDate: fromDate || undefined,
    toDate: toDate || undefined,
    page,
    pageSize,
  };

  // Fetch transactions using RTK Query
  const { data, error, isLoading, isFetching } = useSearchTransactionsQuery(params);

  const handleSearch = (e: React.FormEvent) => {
    e.preventDefault();
    setPage(1); // Reset to first page on new search
  };

  const handleReset = () => {
    setStatusCode('');
    setMethod('');
    setFromDate('');
    setToDate('');
    setPage(1);
  };

  const totalPages = data ? Math.ceil(data.totalCount / pageSize) : 0;

  return (
    <div style={styles.container}>
      <h1 style={styles.title}>Transaction Search</h1>

      {/* Search filters */}
      <form onSubmit={handleSearch} style={styles.filterForm}>
        <div style={styles.filterRow}>
          <div style={styles.filterGroup}>
            <label htmlFor="method" style={styles.label}>
              Method
            </label>
            <select
              id="method"
              value={method}
              onChange={(e) => setMethod(e.target.value)}
              style={styles.select}
            >
              <option value="">All</option>
              <option value="GET">GET</option>
              <option value="POST">POST</option>
              <option value="PUT">PUT</option>
              <option value="DELETE">DELETE</option>
              <option value="PATCH">PATCH</option>
              <option value="HEAD">HEAD</option>
              <option value="OPTIONS">OPTIONS</option>
            </select>
          </div>

          <div style={styles.filterGroup}>
            <label htmlFor="statusCode" style={styles.label}>
              Status Code
            </label>
            <input
              id="statusCode"
              type="text"
              value={statusCode}
              onChange={(e) => setStatusCode(e.target.value)}
              placeholder="e.g. 200, 404"
              style={styles.input}
            />
          </div>

          <div style={styles.filterGroup}>
            <label htmlFor="fromDate" style={styles.label}>
              From Date
            </label>
            <input
              id="fromDate"
              type="date"
              value={fromDate}
              onChange={(e) => setFromDate(e.target.value)}
              style={styles.input}
            />
          </div>

          <div style={styles.filterGroup}>
            <label htmlFor="toDate" style={styles.label}>
              To Date
            </label>
            <input
              id="toDate"
              type="date"
              value={toDate}
              onChange={(e) => setToDate(e.target.value)}
              style={styles.input}
            />
          </div>
        </div>

        <div style={styles.buttonRow}>
          <button type="submit" style={styles.buttonPrimary}>
            Search
          </button>
          <button type="button" onClick={handleReset} style={styles.buttonSecondary}>
            Reset
          </button>
        </div>
      </form>

      {/* Loading state */}
      {isLoading && <div style={styles.message}>Loading transactions...</div>}

      {/* Error state */}
      {error && (
        <div style={styles.error}>
          Failed to load transactions. Please try again.
        </div>
      )}

      {/* Results */}
      {data && (
        <>
          <div style={styles.resultInfo}>
            Found {data.totalCount} transaction{data.totalCount !== 1 ? 's' : ''}
            {isFetching && <span style={styles.fetching}> (updating...)</span>}
          </div>

          {data.data.length === 0 ? (
            <div style={styles.message}>No transactions found matching your criteria.</div>
          ) : (
            <div style={styles.tableContainer}>
              <table style={styles.table}>
                <thead>
                  <tr style={styles.headerRow}>
                    <th style={styles.th}>Timestamp (UTC)</th>
                    <th style={styles.th}>Method</th>
                    <th style={styles.th}>Status</th>
                    <th style={styles.th}>URL Path</th>
                    <th style={styles.th}>Size (bytes)</th>
                    <th style={styles.th}>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {data.data.map((tx) => {
                    // Base64 encode the s3Key for the URL
                    const s3KeyBase64 = btoa(tx.s3Key);

                    return (
                      <tr key={tx.s3Key} style={styles.row}>
                        <td style={styles.td}>
                          {new Date(tx.timestampUtc).toLocaleString()}
                        </td>
                        <td style={styles.td}>
                          <span style={styles.methodBadge}>{tx.method}</span>
                        </td>
                        <td style={styles.td}>
                          <span
                            style={{
                              ...styles.statusBadge,
                              ...(tx.statusCode >= 200 && tx.statusCode < 300
                                ? styles.statusSuccess
                                : tx.statusCode >= 400 && tx.statusCode < 500
                                ? styles.statusWarning
                                : styles.statusError),
                            }}
                          >
                            {tx.statusCode}
                          </span>
                        </td>
                        <td style={styles.td}>
                          <span style={styles.urlPath}>{tx.urlPath}</span>
                        </td>
                        <td style={styles.td}>{tx.fileSizeBytes.toLocaleString()}</td>
                        <td style={styles.td}>
                          <Link
                            to={`/transactions/${encodeURIComponent(s3KeyBase64)}`}
                            style={styles.link}
                          >
                            View Details
                          </Link>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}

          {/* Pagination */}
          {totalPages > 1 && (
            <div style={styles.pagination}>
              <button
                onClick={() => setPage(Math.max(1, page - 1))}
                disabled={page === 1}
                style={styles.paginationButton}
              >
                Previous
              </button>
              <span style={styles.pageInfo}>
                Page {page} of {totalPages}
              </span>
              <button
                onClick={() => setPage(Math.min(totalPages, page + 1))}
                disabled={page === totalPages}
                style={styles.paginationButton}
              >
                Next
              </button>
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
  title: {
    fontSize: '1.75rem',
    marginBottom: '1.5rem',
    color: '#2c3e50',
  },
  filterForm: {
    marginBottom: '2rem',
    padding: '1.5rem',
    backgroundColor: '#f8f9fa',
    borderRadius: '6px',
  },
  filterRow: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))',
    gap: '1rem',
    marginBottom: '1rem',
  },
  filterGroup: {
    display: 'flex',
    flexDirection: 'column',
    gap: '0.5rem',
  },
  label: {
    fontSize: '0.9rem',
    fontWeight: 500,
    color: '#555',
  },
  input: {
    padding: '0.5rem',
    border: '1px solid #ddd',
    borderRadius: '4px',
    fontSize: '0.95rem',
  },
  select: {
    padding: '0.5rem',
    border: '1px solid #ddd',
    borderRadius: '4px',
    fontSize: '0.95rem',
    backgroundColor: '#fff',
  },
  buttonRow: {
    display: 'flex',
    gap: '0.75rem',
  },
  buttonPrimary: {
    padding: '0.625rem 1.5rem',
    backgroundColor: '#3498db',
    color: '#fff',
    border: 'none',
    borderRadius: '4px',
    fontSize: '0.95rem',
    fontWeight: 500,
  },
  buttonSecondary: {
    padding: '0.625rem 1.5rem',
    backgroundColor: '#95a5a6',
    color: '#fff',
    border: 'none',
    borderRadius: '4px',
    fontSize: '0.95rem',
    fontWeight: 500,
  },
  message: {
    padding: '1rem',
    textAlign: 'center',
    color: '#666',
    fontSize: '1rem',
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
  },
  methodBadge: {
    display: 'inline-block',
    padding: '0.25rem 0.5rem',
    backgroundColor: '#3498db',
    color: '#fff',
    borderRadius: '3px',
    fontSize: '0.8rem',
    fontWeight: 500,
  },
  statusBadge: {
    display: 'inline-block',
    padding: '0.25rem 0.5rem',
    borderRadius: '3px',
    fontSize: '0.8rem',
    fontWeight: 500,
  },
  statusSuccess: {
    backgroundColor: '#2ecc71',
    color: '#fff',
  },
  statusWarning: {
    backgroundColor: '#f39c12',
    color: '#fff',
  },
  statusError: {
    backgroundColor: '#e74c3c',
    color: '#fff',
  },
  urlPath: {
    fontFamily: 'monospace',
    fontSize: '0.85rem',
  },
  link: {
    color: '#3498db',
    textDecoration: 'none',
    fontWeight: 500,
  },
  pagination: {
    display: 'flex',
    justifyContent: 'center',
    alignItems: 'center',
    gap: '1rem',
    marginTop: '1.5rem',
  },
  paginationButton: {
    padding: '0.5rem 1rem',
    backgroundColor: '#3498db',
    color: '#fff',
    border: 'none',
    borderRadius: '4px',
    fontSize: '0.9rem',
  },
  pageInfo: {
    fontSize: '0.95rem',
    color: '#666',
  },
};

export default TransactionListPage;
