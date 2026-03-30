#!/bin/bash

# DataViewer Admin Password Reset Script
# Generates a BCrypt hash for a password and updates the admin user in the database

set -e

# Default values
DB_HOST="${DB_HOST:-winhost}"
DB_NAME="${DB_NAME:-dataviewer}"
DB_USER="${DB_USER:-dview}"
DB_PASSWORD="${DB_PASSWORD:-dview01}"
ADMIN_USERNAME="${ADMIN_USERNAME:-admin}"

# Check if password is provided
if [ -z "$1" ]; then
    echo "Usage: $0 <new_password>"
    echo ""
    echo "Example: $0 'Admin123!'"
    echo ""
    echo "Environment variables (optional):"
    echo "  DB_HOST      - PostgreSQL host (default: winhost)"
    echo "  DB_NAME      - Database name (default: dataviewer)"
    echo "  DB_USER      - Database user (default: dview)"
    echo "  DB_PASSWORD  - Database password (default: dview01)"
    echo "  ADMIN_USERNAME - Admin username to update (default: admin)"
    exit 1
fi

NEW_PASSWORD="$1"

echo "Generating BCrypt hash for password..."

# Generate BCrypt hash using Python with bcrypt library
# Pass password as argument to avoid shell expansion issues
HASH=$(python3 - "$NEW_PASSWORD" << 'PYTHON_SCRIPT'
import bcrypt
import sys

password = sys.argv[1].encode('utf-8')
salt = bcrypt.gensalt(rounds=12)
hashed = bcrypt.hashpw(password, salt)
print(hashed.decode('utf-8'))
PYTHON_SCRIPT
)

if [ -z "$HASH" ]; then
    echo "Error: Failed to generate BCrypt hash"
    echo "Make sure Python 3 and bcrypt library are installed:"
    echo "  pip3 install bcrypt"
    exit 1
fi

echo "Generated hash: $HASH"
echo ""
echo "Updating admin user in database..."

# Update the admin user's password
PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -U "$DB_USER" -d "$DB_NAME" << SQL
UPDATE "Users"
SET "PasswordHash" = '$HASH',
    "FailedLoginCount" = 0,
    "IsLocked" = false
WHERE "UserName" = '$ADMIN_USERNAME';
SQL

if [ $? -eq 0 ]; then
    echo ""
    echo "✓ Admin user password updated successfully!"
    echo ""
    echo "You can now login with:"
    echo "  Username: $ADMIN_USERNAME"
    echo "  Password: $NEW_PASSWORD"
else
    echo ""
    echo "✗ Error: Failed to update password in database"
    exit 1
fi
