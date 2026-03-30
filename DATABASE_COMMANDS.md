# PostgreSQL Commands for DataViewer

## Important: Case-Sensitive Table Names!

EF Core created tables with **PascalCase** names using quotes. PostgreSQL requires quotes to preserve case.

### ❌ Wrong (won't work)
```sql
select * from Users;
-- ERROR: relation "users" does not exist
```

### ✅ Correct (with quotes)
```sql
SELECT * FROM "Users";
```

---

## Useful Commands

### Connect to Database
```bash
psql -h winhost -U dview -d dataviewer
```

### List Tables
```sql
\d

-- You'll see:
-- Users
-- CredentialProfiles
-- RefreshTokens
-- AuditLogEntries
-- UserPreferences
-- SystemSettings
-- __EFMigrationsHistory
```

### View Table Structure
```sql
\d "Users"
\d "CredentialProfiles"
\d "AuditLogEntries"
```

---

## Check If Admin User Exists

```sql
SELECT "Id", "UserName", "Email", "Role", "IsLocked", "FailedLoginCount"
FROM "Users"
WHERE "UserName" = 'admin';
```

**Expected Result**:
- If user exists: Shows one row with admin user
- If empty: User wasn't created, need to insert

---

## Create Admin User

```sql
INSERT INTO "Users" (
    "Id",
    "UserName",
    "Email",
    "PasswordHash",
    "Role",
    "IsLocked",
    "FailedLoginCount",
    "CreatedAt"
)
VALUES (
    gen_random_uuid(),
    'admin',
    'admin@dataviewer.local',
    '$2a$12$LQv3c1yqBWVHxkd0LHAkCOYz6TtxMQJqhN8/LewY5ztP.lA8qCq4C',
    1,
    false,
    0,
    NOW()
);
```

**Verify it was created**:
```sql
SELECT * FROM "Users";
```

---

## Troubleshooting Login Issues

### 1. Check User Exists
```sql
SELECT "UserName", "Role", "IsLocked", "FailedLoginCount"
FROM "Users"
WHERE "UserName" = 'admin';
```

**What to check**:
- `IsLocked` should be `false` (not `t` or `true`)
- `FailedLoginCount` should be `0` or low number
- `Role` should be `1` (Admin)

### 2. Check Password Hash
```sql
SELECT "UserName", LENGTH("PasswordHash") as hash_length
FROM "Users"
WHERE "UserName" = 'admin';
```

**Expected**: hash_length should be 60 (BCrypt hash)

### 3. Check SystemSettings
```sql
SELECT * FROM "SystemSettings";
```

**Expected**: One row with Id = 1
If empty, insert default:
```sql
INSERT INTO "SystemSettings" (
    "Id",
    "JwtAccessTokenMinutes",
    "JwtRefreshTokenHours",
    "BodySizeCapMb",
    "LockoutThreshold",
    "UpdatedAt"
)
VALUES (
    1,
    15,
    24,
    10,
    5,
    NOW()
);
```

### 4. View Failed Login Attempts
```sql
SELECT "UserName", "ActionType", "Timestamp", "IpAddress", "Parameters"
FROM "AuditLogEntries" a
JOIN "Users" u ON a."UserId" = u."Id"
WHERE a."ActionType" IN (4, 5)  -- Login, FailedLogin
ORDER BY a."Timestamp" DESC
LIMIT 10;
```

**Action Types**:
- 4 = Login (success)
- 5 = FailedLogin
- 6 = Logout

### 5. Reset Failed Login Count
If account is locked:
```sql
UPDATE "Users"
SET "IsLocked" = false,
    "FailedLoginCount" = 0,
    "LockoutUntil" = NULL
WHERE "UserName" = 'admin';
```

---

## Check Backend Logs

```bash
# Docker backend logs
docker logs dataviewer-api -f --tail 50

# Look for lines like:
# [INF] Login attempt for user admin from IP 127.0.0.1
# [WRN] Login failed for user admin
# [ERR] Authentication error: Invalid username or password
```

---

## Test Credentials

**Username**: `admin`
**Password**: `Admin123!`
**BCrypt Hash**: `$2a$12$LQv3c1yqBWVHxkd0LHAkCOYz6TtxMQJqhN8/LewY5ztP.lA8qCq4C`

This hash was generated with BCrypt work factor 12.

---

## View All Data

```sql
-- All users
SELECT * FROM "Users";

-- All credential profiles
SELECT * FROM "CredentialProfiles";

-- Recent audit entries
SELECT * FROM "AuditLogEntries" ORDER BY "Timestamp" DESC LIMIT 20;

-- User preferences
SELECT * FROM "UserPreferences";

-- System settings
SELECT * FROM "SystemSettings";
```

---

## Delete Test User (if needed)

```sql
DELETE FROM "Users" WHERE "UserName" = 'admin';
```

Then recreate with the INSERT statement above.

---

## Common Issues

### Issue: "relation users does not exist"
**Solution**: Use quotes: `"Users"` not `Users`

### Issue: Login returns 401
**Check**:
1. User exists: `SELECT * FROM "Users" WHERE "UserName" = 'admin';`
2. Password hash is correct (60 chars)
3. User not locked: `"IsLocked" = false`
4. Backend logs for error details

### Issue: No audit entries
Backend might not be recording login attempts. Check backend logs:
```bash
docker logs dataviewer-api --tail 100
```

---

## Quick Test Script

```bash
# Check everything is set up correctly
psql -h winhost -U dview -d dataviewer << 'EOF'
-- Check tables exist
\d

-- Check user exists
SELECT 'User exists:' as check, COUNT(*) as count
FROM "Users" WHERE "UserName" = 'admin';

-- Check SystemSettings exists
SELECT 'SystemSettings exists:' as check, COUNT(*) as count
FROM "SystemSettings";

-- View user details
SELECT "UserName", "Email", "Role", "IsLocked", "FailedLoginCount"
FROM "Users" WHERE "UserName" = 'admin';
EOF
```

---

**Remember**: Always use **double quotes** around table and column names in PostgreSQL!
