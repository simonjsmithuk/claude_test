# DataViewer - Quick Start Guide

## 🚀 Start the Application (2 minutes)

### Backend API (Already Running)
```bash
# Check if running
curl http://localhost:8080/health

# If not running, start it
docker-compose up -d

# View logs
docker-compose logs -f api
```

**Backend URL**: http://localhost:8080
**Swagger**: http://localhost:8080/swagger
**Health**: http://localhost:8080/health

---

### Frontend UI (First Time Setup)
```bash
# Navigate to frontend
cd frontend

# Install dependencies (only needed once)
npm install

# Start development server
npm run dev
```

**Frontend URL**: http://localhost:3000

---

## 📝 Create Test Admin User

The database starts empty. You need to create a test user:

```bash
# Connect to PostgreSQL
psql -h winhost -U dview -d dataviewer

# Run this SQL
INSERT INTO "Users" ("Id", "UserName", "Email", "PasswordHash", "Role", "IsLocked", "FailedLoginCount", "CreatedAt")
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

**Test Credentials**:
- Username: `admin`
- Password: `Admin123!`

---

## 🧪 Test the Application

### 1. Test Backend API with curl
```bash
# Login
curl -X POST http://localhost:8080/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"userName":"admin","password":"Admin123!"}'

# Save the token from response
TOKEN="<paste_access_token_here>"

# Test protected endpoint
curl http://localhost:8080/api/v1/credential-profiles \
  -H "Authorization: Bearer $TOKEN"
```

### 2. Test Frontend UI
1. Open http://localhost:3000
2. Login with `admin` / `Admin123!`
3. Browse to Transactions page
4. Try searching/filtering

---

## 📂 Project Structure

```
/home/simon/code_wsl/claude_test/
├── src/
│   ├── DataViewer.Domain/          (Entities, enums, exceptions)
│   ├── DataViewer.Application/     (24 use cases)
│   ├── DataViewer.Infrastructure/  (Repositories, services, EF Core)
│   └── DataViewer.API/             (4 controllers, 15 endpoints)
├── frontend/
│   └── src/
│       ├── pages/                  (Login, Transactions)
│       ├── components/             (PrivateRoute, Layout)
│       ├── redux/                  (Store, slices, RTK Query)
│       └── services/               (API client)
├── docker-compose.yml
├── Dockerfile
├── .env
└── Documentation:
    ├── QUICK_START.md              (This file)
    ├── SESSION_SUMMARY.md          (Complete session details)
    ├── MILESTONE_BACKEND_API_POC.md (Backend milestone doc)
    └── COMPLETION_STATUS.md        (Overall status)
```

---

## 🔑 Key URLs

| Service | URL | Status |
|---------|-----|--------|
| Backend API | http://localhost:8080 | ✅ Running |
| Swagger UI | http://localhost:8080/swagger | ⚠️ Empty (cache issue) |
| Health Check | http://localhost:8080/health | ✅ Working |
| Frontend UI | http://localhost:3000 | 🟡 Needs `npm install` |
| Database | winhost:5432/dataviewer | ✅ Connected |

---

## 📋 Available API Endpoints

### Authentication (`/api/v1/auth`)
- `POST /login` - Get JWT tokens
- `POST /logout` - Revoke token
- `POST /refresh` - Refresh access token

### Credential Profiles (`/api/v1/credential-profiles`)
- `GET /` - List all profiles
- `POST /` - Create profile
- `PUT /{id}` - Update profile
- `DELETE /{id}` - Delete profile
- `POST /{id}/test` - Test S3 connection
- `POST /{id}/activate` - Set as active

### Transactions (`/api/v1/transactions`)
- `GET /` - Search with filters
- `GET /{s3Key}` - Get detail

### Admin (`/api/v1/admin`)
- `GET /preferences` - Get user preferences
- `PUT /preferences` - Update preferences
- `GET /settings` - Get system settings (Admin)
- `PUT /settings` - Update settings (Admin)

---

## 🛠️ Common Commands

### Backend
```bash
# View API logs
docker-compose logs -f api

# Restart API
docker-compose restart api

# Stop everything
docker-compose down

# Rebuild and restart
docker-compose down
docker build --no-cache -t dataviewer-api:latest .
docker-compose up -d
```

### Frontend
```bash
cd frontend

# Start dev server
npm run dev

# Build for production
npm run build

# Run tests
npm test
```

### Database
```bash
# Apply migrations
cd src/DataViewer.Infrastructure
export DATAVIEWER_DESIGN_TIME_CONNECTION="Host=winhost;Database=dataviewer;Username=dview;Password=dview01"
export DATAVIEWER_DB_PROVIDER="postgresql"
dotnet ef database update --context AppDbContext --startup-project ../DataViewer.API

# View migration history
dotnet ef migrations list --context AppDbContext --startup-project ../DataViewer.API
```

---

## 🐛 Troubleshooting

### Backend not starting
```bash
# Check logs
docker-compose logs api

# Common issues:
# - JWT__SECRET not set → check .env file
# - Database connection failed → check winhost is accessible
# - Port 8080 already in use → stop other services
```

### Frontend build errors
```bash
# Clear node_modules and reinstall
rm -rf node_modules package-lock.json
npm install
```

### API endpoints not showing in Swagger
This is a known Docker cache issue. **Workaround**:
```bash
# Run API directly without Docker
cd src/DataViewer.API
dotnet run

# Then access Swagger at http://localhost:5000/swagger
```

---

## ✅ What's Working

- ✅ Backend API (15 endpoints)
- ✅ Database (6 tables, migrations applied)
- ✅ JWT Authentication
- ✅ Docker deployment
- ✅ Health checks
- ✅ Frontend foundation (Login, Transactions)

## 🚧 What's Not Complete

- ⚠️ Swagger UI (cache issue - endpoints not showing)
- ⏸️ Admin UI pages (Profiles, Settings, Audit)
- ⏸️ Frontend dependencies not installed yet
- ⏸️ No seed data (need to manually create test user)

---

## 📚 More Documentation

- **[SESSION_SUMMARY.md](SESSION_SUMMARY.md)** - Complete session details
- **[MILESTONE_BACKEND_API_POC.md](MILESTONE_BACKEND_API_POC.md)** - Backend milestone
- **[COMPLETION_STATUS.md](COMPLETION_STATUS.md)** - Overall status
- **[POC_MILESTONE_PLAN.md](POC_MILESTONE_PLAN.md)** - Original plan

---

**Status**: ✅ Backend Complete | 🟡 Frontend 60% Complete | **Ready to Test!**
