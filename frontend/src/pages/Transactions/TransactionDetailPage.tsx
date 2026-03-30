/**
 * TransactionDetailPage.tsx
 * =========================
 * Transaction detail view showing request/response headers and bodies.
 *
 * Features:
 *  - Display full transaction metadata
 *  - Show request and response sections with headers and body
 *  - Syntax highlighting for JSON/XML bodies
 *  - Copy-to-clipboard functionality for bodies
 *
 * Uses:
 *  - useGetTransactionDetailQuery hook from RTK Query
 */

import React, { useState } from 'react';
import { useParams, Link } from 'react-router-dom';
import { useGetTransactionDetailQuery } from '../../redux/api/dataViewerApi';
import type { TransactionBodySection } from '../../redux/api/dataViewerApi';

const TransactionDetailPage: React.FC = () => {
  const { s3KeyBase64 } = useParams<{ s3KeyBase64: string }>();

  const { data, error, isLoading } = useGetTransactionDetailQuery(s3KeyBase64 || '');

  if (isLoading) {
    return (
      <div style={styles.container}>
        <div style={styles.message}>Loading transaction details...</div>
      </div>
    );
  }

  if (error) {
    return (
      <div style={styles.container}>
        <div style={styles.error}>
          Failed to load transaction details. The transaction may not exist or you may not
          have permission to view it.
        </div>
        <Link to="/transactions" style={styles.backLink}>
          Back to Transactions
        </Link>
      </div>
    );
  }

  if (!data) {
    return (
      <div style={styles.container}>
        <div style={styles.message}>No data available.</div>
      </div>
    );
  }

  const { metadata, request, response } = data;

  return (
    <div style={styles.container}>
      <div style={styles.header}>
        <h1 style={styles.title}>Transaction Details</h1>
        <Link to="/transactions" style={styles.backLink}>
          Back to Transactions
        </Link>
      </div>

      {/* Metadata section */}
      <section style={styles.section}>
        <h2 style={styles.sectionTitle}>Metadata</h2>
        <div style={styles.metadataGrid}>
          <div style={styles.metadataItem}>
            <span style={styles.metadataLabel}>S3 Key:</span>
            <span style={styles.metadataValue}>{metadata.s3Key}</span>
          </div>
          <div style={styles.metadataItem}>
            <span style={styles.metadataLabel}>Timestamp (UTC):</span>
            <span style={styles.metadataValue}>
              {new Date(metadata.timestampUtc).toLocaleString()}
            </span>
          </div>
          <div style={styles.metadataItem}>
            <span style={styles.metadataLabel}>Compressed Size:</span>
            <span style={styles.metadataValue}>
              {metadata.compressedSizeBytes.toLocaleString()} bytes
            </span>
          </div>
          <div style={styles.metadataItem}>
            <span style={styles.metadataLabel}>Decompressed Size:</span>
            <span style={styles.metadataValue}>
              {metadata.decompressedSizeBytes.toLocaleString()} bytes
            </span>
          </div>
          <div style={styles.metadataItem}>
            <span style={styles.metadataLabel}>S3 Last Modified:</span>
            <span style={styles.metadataValue}>
              {new Date(metadata.s3LastModified).toLocaleString()}
            </span>
          </div>
        </div>
      </section>

      {/* Request section */}
      <section style={styles.section}>
        <h2 style={styles.sectionTitle}>Request</h2>
        <TransactionSection section={request} isRequest />
      </section>

      {/* Response section */}
      <section style={styles.section}>
        <h2 style={styles.sectionTitle}>Response</h2>
        <TransactionSection section={response} isRequest={false} />
      </section>
    </div>
  );
};

// ---------------------------------------------------------------------------
// TransactionSection component
// ---------------------------------------------------------------------------

interface TransactionSectionProps {
  section: TransactionBodySection;
  isRequest: boolean;
}

const TransactionSection: React.FC<TransactionSectionProps> = ({ section, isRequest }) => {
  const [copied, setCopied] = useState(false);

  const handleCopyBody = () => {
    if (section.body) {
      navigator.clipboard.writeText(section.body);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    }
  };

  // Format the body for display based on content type
  const formatBody = (body: string | null, contentType: string | null): string => {
    if (!body) return '';

    if (contentType === 'json') {
      try {
        return JSON.stringify(JSON.parse(body), null, 2);
      } catch {
        return body;
      }
    }

    return body;
  };

  const formattedBody = formatBody(section.body, section.bodyContentType);

  return (
    <div style={styles.transactionSection}>
      {/* Status line / Request line */}
      <div style={styles.statusLine}>
        {isRequest ? (
          <span>
            <strong>{section.method}</strong> {section.urlPath} {section.httpVersion}
          </span>
        ) : (
          <span>
            {section.httpVersion} <strong>{section.statusCode}</strong> {section.statusText}
          </span>
        )}
      </div>

      {/* Headers */}
      <div style={styles.headersSection}>
        <h3 style={styles.subsectionTitle}>Headers</h3>
        <div style={styles.headersGrid}>
          {Object.entries(section.headers).map(([name, value]) => (
            <div key={name} style={styles.headerRow}>
              <span style={styles.headerName}>{name}:</span>
              <span style={styles.headerValue}>{value}</span>
            </div>
          ))}
          {Object.keys(section.headers).length === 0 && (
            <div style={styles.emptyMessage}>No headers</div>
          )}
        </div>
      </div>

      {/* Body */}
      <div style={styles.bodySection}>
        <div style={styles.bodyHeader}>
          <h3 style={styles.subsectionTitle}>Body</h3>
          {section.body && (
            <button onClick={handleCopyBody} style={styles.copyButton}>
              {copied ? 'Copied!' : 'Copy'}
            </button>
          )}
        </div>
        {section.body ? (
          <>
            {section.isBodyTruncated && (
              <div style={styles.truncatedWarning}>
                Body has been truncated due to size limits
              </div>
            )}
            <pre style={styles.bodyPre}>
              <code>{formattedBody}</code>
            </pre>
          </>
        ) : (
          <div style={styles.emptyMessage}>No body</div>
        )}
      </div>
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
    marginBottom: '2rem',
  },
  title: {
    fontSize: '1.75rem',
    color: '#2c3e50',
  },
  backLink: {
    color: '#3498db',
    textDecoration: 'none',
    fontWeight: 500,
  },
  message: {
    padding: '2rem',
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
  section: {
    marginBottom: '2rem',
  },
  sectionTitle: {
    fontSize: '1.25rem',
    marginBottom: '1rem',
    color: '#2c3e50',
    borderBottom: '2px solid #3498db',
    paddingBottom: '0.5rem',
  },
  metadataGrid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(300px, 1fr))',
    gap: '1rem',
    padding: '1rem',
    backgroundColor: '#f8f9fa',
    borderRadius: '6px',
  },
  metadataItem: {
    display: 'flex',
    flexDirection: 'column',
    gap: '0.25rem',
  },
  metadataLabel: {
    fontSize: '0.85rem',
    fontWeight: 600,
    color: '#666',
    textTransform: 'uppercase',
  },
  metadataValue: {
    fontSize: '0.95rem',
    color: '#333',
    fontFamily: 'monospace',
  },
  transactionSection: {
    border: '1px solid #ddd',
    borderRadius: '6px',
    overflow: 'hidden',
  },
  statusLine: {
    padding: '1rem',
    backgroundColor: '#34495e',
    color: '#fff',
    fontFamily: 'monospace',
    fontSize: '0.95rem',
  },
  headersSection: {
    padding: '1rem',
    borderBottom: '1px solid #ddd',
  },
  subsectionTitle: {
    fontSize: '1rem',
    marginBottom: '0.75rem',
    color: '#555',
    fontWeight: 600,
  },
  headersGrid: {
    display: 'flex',
    flexDirection: 'column',
    gap: '0.5rem',
  },
  headerRow: {
    display: 'flex',
    gap: '0.5rem',
    fontSize: '0.9rem',
    fontFamily: 'monospace',
  },
  headerName: {
    fontWeight: 600,
    color: '#555',
    minWidth: '200px',
  },
  headerValue: {
    color: '#333',
    wordBreak: 'break-all',
  },
  bodySection: {
    padding: '1rem',
  },
  bodyHeader: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: '0.75rem',
  },
  copyButton: {
    padding: '0.375rem 0.75rem',
    backgroundColor: '#3498db',
    color: '#fff',
    border: 'none',
    borderRadius: '4px',
    fontSize: '0.85rem',
    fontWeight: 500,
  },
  truncatedWarning: {
    padding: '0.5rem',
    backgroundColor: '#fff3cd',
    color: '#856404',
    borderRadius: '4px',
    marginBottom: '0.75rem',
    fontSize: '0.85rem',
    border: '1px solid #ffeaa7',
  },
  bodyPre: {
    backgroundColor: '#f8f9fa',
    border: '1px solid #e1e4e8',
    borderRadius: '4px',
    padding: '1rem',
    overflow: 'auto',
    maxHeight: '600px',
    fontSize: '0.85rem',
    fontFamily: 'monospace',
    lineHeight: 1.5,
  },
  emptyMessage: {
    color: '#999',
    fontStyle: 'italic',
    fontSize: '0.9rem',
  },
};

export default TransactionDetailPage;
