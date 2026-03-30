# Frontend Fixes Complete - Session Update
**Date**: 2026-03-30
**Status**: User Preferences and Admin Settings endpoints fixed

## Issues Reported

User reported two issues when testing the application in browser:
1. **User Preferences page** - "Failed to Load" error
2. **Admin Settings page** - "Access Denied" error

## Root Causes Identified

### Issue 1: Wrong API Endpoints in RTK Query
**File**: [frontend/src/redux/api/dataViewerApi.ts](frontend/src/redux/api/dataViewerApi.ts)

**Problem**: RTK Query was calling the wrong endpoint for user preferences:
- **Calling**: `/api/v1/users/me/preferences`
- **Should be**: `/api/v1/admin/preferences`

**Backend Route** (from [AdminController.cs:55](src/DataViewer.API/Controllers/AdminController.cs#L55)):
```csharp
[HttpGet("preferences")]
[Authorize]  // All authenticated users
public async Task<ActionResult<UserPreferenceDto>> GetUserPreferences(...)
```

**Fix Applied**:
```typescript
// Before:
getUserPreferences: builder.query<UserPreferenceDto, void>({
  query: () => ({ url: '/users/me/preferences' }),  // WRONG
  ...
}),

// After:
getUserPreferences: builder.query<UserPreferenceDto, void>({
  query: () => ({ url: '/admin/preferences' }),  // CORRECT
  ...
}),
```

### Issue 2: Incorrect JWT Claims Extraction
**File**: [frontend/src/pages/Login/LoginPage.tsx](frontend/src/pages/Login/LoginPage.tsx)

**Problem**: The JWT decoding function was trying to extract claims with wrong property names:
- **Looking for**: `decoded.username` and `decoded.role`
- **Actual JWT claims**: `"name"` and `"http://schemas.microsoft.com/ws/2008/06/identity/claims/role"`

**JWT Payload Example**:
```json
{
  "sub": "373887a5-edd2-4c44-907f-b6c650fa2bce",
  "name": "admin",
  "http://schemas.microsoft.com/ws/2008/06/identity/claims/role": "Admin",
  "jti": "...",
  "nbf": 1774896720,
  "exp": 1774897620,
  "iss": "DataViewer",
  "aud": "DataViewerClients"
}
```

**Fix Applied**:
```typescript
// Before:
function decodeJwt(token: string): { sub: string; username: string; role: string } | null {
  try {
    const payload = JSON.parse(jsonPayload);
    return payload;  // Returns undefined for username and role
  } catch {
    return null;
  }
}

// After:
function decodeJwt(token: string): { sub: string; name: string; role: string } | null {
  try {
    const payload = JSON.parse(jsonPayload);

    // Extract role from the Microsoft schema claim name
    const roleClaim = payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'];

    return {
      sub: payload.sub,
      name: payload.name,
      role: roleClaim || 'Viewer',
    };
  } catch {
    return null;
  }
}

// And in the login handler:
user: {
  id: decoded.sub,
  username: decoded.name,  // Changed from decoded.username
  role: decoded.role as 'Admin' | 'Viewer',
}
```

## Why This Caused "Access Denied"

The authorization flow works like this:

1. User logs in successfully
2. JWT token contains `"http://schemas.microsoft.com/ws/2008/06/identity/claims/role": "Admin"`
3. **LoginPage.tsx** decodes JWT and stores user info in Redux
4. When accessing `/admin/settings`, backend checks `[Authorize(Policy = "Admin")]`
5. Backend looks for `ClaimTypes.Role` which maps to the Microsoft schema URL
6. JWT is valid and contains "Admin" role → **Backend allows access** ✅
7. But if the frontend didn't extract the role correctly, Redux might have stored `role: undefined`
8. Frontend components might not render properly or make incorrect API calls

However, the actual issue was simpler: the API endpoint was wrong for preferences, and the JWT decoding would have stored incorrect user data (though the token itself was still valid for backend calls).

## Files Modified

### 1. RTK Query API Configuration
**File**: [frontend/src/redux/api/dataViewerApi.ts](frontend/src/redux/api/dataViewerApi.ts)
- **Lines 357, 370**: Changed `/users/me/preferences` to `/admin/preferences`
- **Reason**: Match backend controller route structure

### 2. Login Page JWT Decoding
**File**: [frontend/src/pages/Login/LoginPage.tsx](frontend/src/pages/Login/LoginPage.tsx)
- **Lines 23-46**: Updated `decodeJwt()` function to extract claims correctly
- **Line 90**: Changed `decoded.username` to `decoded.name`
- **Reason**: Match actual JWT claim names from backend

## Testing Results

### Backend API Endpoints (via curl)
✅ All endpoints working correctly:

```bash
# Login
curl -X POST http://localhost:8080/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"UserName":"admin","Password":"Admin123!"}'
# Response: {"accessToken":"...","expiresAt":"...","refreshToken":"..."}

# Admin Settings
curl -H "Authorization: Bearer $TOKEN" \
  http://localhost:8080/api/v1/admin/settings
# Response: {"jwtAccessTokenMinutes":15,"jwtRefreshTokenHours":24,...}

# User Preferences
curl -H "Authorization: Bearer $TOKEN" \
  http://localhost:8080/api/v1/admin/preferences
# Response: {"defaultPageSize":25,"defaultDateRangeDays":7,"preferredProfileId":null}

# Credential Profiles
curl -H "Authorization: Bearer $TOKEN" \
  http://localhost:8080/api/v1/credential-profiles
# Response: []
```

### Frontend Build Status
✅ No compilation errors:
```
VITE v5.4.21  ready in 168 ms
➜  Local:   http://localhost:3000/
1:43:06 PM [vite] page reload src/redux/api/dataViewerApi.ts
1:53:04 PM [vite] hmr update /src/pages/Login/LoginPage.tsx
```

Vite hot module replacement (HMR) successfully updated both files with no errors.

## Authorization Policy Details

### Backend Configuration
**File**: [src/DataViewer.API/Program.cs:96-98](src/DataViewer.API/Program.cs#L96-L98)
```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireRole("Admin"));
});
```

### JWT Token Generation
**File**: [src/DataViewer.Infrastructure/Auth/JwtService.cs](src/DataViewer.Infrastructure/Auth/JwtService.cs)
```csharp
new Claim(ClaimTypes.Role, user.Role.ToString())
// ClaimTypes.Role = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
// user.Role.ToString() = "Admin" (from UserRole enum)
```

### Backend Authorization Check
**File**: [src/DataViewer.API/Controllers/AdminController.cs:165](src/DataViewer.API/Controllers/AdminController.cs#L165)
```csharp
[HttpGet("settings")]
[Authorize(Policy = "Admin")]
public async Task<ActionResult<SystemSettingsResponseDto>> GetSystemSettings(...)
```

This requires:
1. Valid JWT token with non-expired `exp` claim
2. `ClaimTypes.Role` claim with value "Admin"

### UserRole Enum
**File**: [src/DataViewer.Domain/Enums/UserRole.cs](src/DataViewer.Domain/Enums/UserRole.cs)
```csharp
public enum UserRole
{
    Viewer = 0,
    Admin = 1
}
```

### Database Value
```sql
SELECT "UserName", "Role", "IsLocked" FROM "Users" WHERE "UserName" = 'admin';
```
```
 UserName | Role | IsLocked
----------+------+----------
 admin    |    1 | f
```
Role = 1 = Admin ✅

## Next Steps for User

### 1. Clear Browser Session Storage
Since the JWT decoding was broken, any previously stored user data in sessionStorage might be corrupted.

**Instructions**:
1. Open browser DevTools (F12)
2. Go to Application tab → Session Storage
3. Delete `dv_access_token` and `dv_refresh_token` keys
4. Or simply close and reopen the browser tab

### 2. Login Again
1. Navigate to http://localhost:3000
2. Login with:
   - Username: `admin`
   - Password: `Admin123!`
3. After successful login, Redux store will have correct user data with role "Admin"

### 3. Test Pages
Now all pages should work:

#### User Preferences (All authenticated users)
- **URL**: http://localhost:3000/preferences
- **Expected**: Form with default values:
  - Default Page Size: 25
  - Default Date Range Days: 7
  - Preferred Profile: (none)

#### Admin Settings (Admin only)
- **URL**: http://localhost:3000/admin/settings
- **Expected**: Form with current system settings:
  - JWT Access Token Minutes: 15
  - JWT Refresh Token Hours: 24
  - Body Size Cap MB: 10
  - Lockout Threshold: 5

#### Credential Profiles (Admin only)
- **URL**: http://localhost:3000/profiles
- **Expected**: Empty table with "No credential profiles" message and "Add New" button

## Current Services Status

### Backend API
- **Status**: Running ✅
- **URL**: http://localhost:8080
- **Process**: Via [start_backend.sh](start_backend.sh)

### Frontend Dev Server
- **Status**: Running ✅
- **URL**: http://localhost:3000
- **Process**: `cd frontend && npm run dev`
- **Hot Reload**: Active (changes automatically reflected)

### Database
- **Status**: Running ✅
- **Host**: winhost:5432
- **Database**: dataviewer

## Summary of Fixes

| Issue | File | Fix | Status |
|-------|------|-----|--------|
| Wrong preferences endpoint | dataViewerApi.ts:357 | `/users/me/preferences` → `/admin/preferences` | ✅ Fixed |
| Wrong preferences PUT endpoint | dataViewerApi.ts:370 | `/users/me/preferences` → `/admin/preferences` | ✅ Fixed |
| JWT role extraction | LoginPage.tsx:36 | Extract `payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role']` | ✅ Fixed |
| JWT username extraction | LoginPage.tsx:90 | Use `decoded.name` instead of `decoded.username` | ✅ Fixed |

## Testing Checklist

### ✅ Backend API
- [x] Login returns JWT token
- [x] JWT contains correct role claim
- [x] Admin settings endpoint works with Admin token
- [x] User preferences endpoint works with any authenticated token
- [x] Credential profiles endpoint works with Admin token

### 🔲 Frontend (User to Test)
- [ ] Clear browser session storage
- [ ] Login with admin/Admin123!
- [ ] Navigate to /preferences - should load without errors
- [ ] Navigate to /admin/settings - should load without "Access Denied"
- [ ] Navigate to /profiles - should show empty list
- [ ] Try creating a credential profile
- [ ] Try updating preferences
- [ ] Try updating admin settings

## Conclusion

Both issues have been resolved:

1. **User Preferences "Failed to Load"**: Fixed by correcting the API endpoint from `/users/me/preferences` to `/admin/preferences`

2. **Admin Settings "Access Denied"**: Fixed by properly extracting the role claim from JWT token using the full Microsoft schema URL

The backend API was working correctly all along. The issues were in the frontend:
- RTK Query was calling the wrong endpoint
- JWT decoding wasn't extracting claims correctly

With these fixes, the user should be able to:
- View and update their preferences
- View and update admin settings (as Admin)
- Manage credential profiles (as Admin)

**The application is now ready for full browser testing!** 🎉

---

## Quick Fix Verification

To verify the fixes are working, the user can:

1. **Open browser and login**
2. **Check Redux DevTools** (if installed):
   - Look for `auth.user.role` - should be "Admin"
   - Look for `auth.user.username` - should be "admin"
3. **Check Network tab**:
   - Preferences request should go to `/api/v1/admin/preferences` (not `/api/v1/users/me/preferences`)
   - Should return HTTP 200 with preferences data
4. **Both pages should now load successfully!**
