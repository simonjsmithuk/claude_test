# DataViewer POC Milestone & Story Grouping Plan

**Date:** 2026-03-27
**Purpose:** Identify executable POC stopping points and group remaining tasks into deployable stories

---

## Current Progress

**Completed:** 15 tasks (TASK-001 through TASK-015)
**In Progress:** TASK-016 (Transaction Parser)
**Remaining:** 28 tasks (TASK-017 through TASK-044)

---

## POC Milestone Options

### Option A: Backend API POC (Recommended)
**Stop After:** TASK-031 (API Controllers complete)
**Estimated Time:** ~2.5 hours (16 remaining backend tasks)

**What's Included:**
- ✅ Complete domain model with all entities and value objects
- ✅ Complete infrastructure layer (database, S3, encryption, JWT)
- ✅ Complete application layer (all use cases)
- ✅ Complete REST API with all endpoints
- ✅ Swagger documentation
- ✅ Health checks

**What's Deployable:**
- Backend API Docker container
- PostgreSQL database container
- Can test all endpoints via Swagger UI or Postman
- Full audit trail operational

**Deployment:**
```bash
# After TASK-031 completes
cd src/DataViewer.API
dotnet build
dotnet run

# Or with Docker (after TASK-044)
docker-compose up api db
```

**Testing POC:**
```bash
# Health check
curl http://localhost:8080/health

# Login
curl -X POST http://localhost:8080/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"admin123"}'

# Search transactions (with token)
curl http://localhost:8080/api/v1/transactions \
  -H "Authorization: Bearer {token}"

# Swagger UI
open http://localhost:8080/swagger
```

**Missing:**
- ❌ Frontend UI (can use Swagger or curl)
- ❌ Docker configuration (manual deployment only)

---

### Option B: Full Application POC
**Stop After:** TASK-043 (Frontend complete, before Docker)
**Estimated Time:** ~5-6 hours (27 remaining tasks)

**What's Included:**
- Everything from Option A
- Complete React frontend with all pages
- Full authentication flow
- Transaction search and detail views
- Credential profile management UI
- Admin settings UI
- Audit log viewer

**What's Deployable:**
- Backend API + Frontend dev server (Vite)
- Manual deployment only (no Docker yet)

**Deployment:**
```bash
# Terminal 1: Backend
cd src/DataViewer.API
dotnet run

# Terminal 2: Frontend
cd frontend
npm run dev

# Open browser
open http://localhost:5173
```

**Missing:**
- ❌ Docker configuration (manual deployment only)

---

### Option C: Production-Ready POC (Complete)
**Stop After:** TASK-044 (Docker complete)
**Estimated Time:** ~6 hours (all 28 remaining tasks)

**What's Included:**
- Everything from Option B
- Docker Compose configuration
- Multi-stage Docker builds
- Environment variable management
- Production-ready deployment

**Deployment:**
```bash
# Single command deployment
docker-compose up --build

# Access application
open http://localhost:3000
```

---

## Recommended Approach: Option A (Backend API POC)

### Why This is the Best POC:
1. **Fastest to Working System** - ~2.5 hours vs 6 hours
2. **Testable Core Functionality** - All business logic operational
3. **Swagger UI Provides Testing Interface** - No frontend needed for validation
4. **Docker Available** - Can deploy with Docker after TASK-044 (4 more hours)
5. **Natural Checkpoint** - Clean separation at API layer completion

### POC Success Criteria:
- [x] Database schema created and migrations applied
- [x] All domain entities and business logic implemented
- [ ] All API endpoints operational (TASK-025 through TASK-031)
- [ ] JWT authentication working
- [ ] S3 transaction retrieval working
- [ ] Audit logging operational
- [ ] Swagger documentation accessible
- [ ] Health checks responding

---

## Story Grouping for Remaining Work

After completing the Backend API POC (TASK-031), group remaining tasks into three stories:

### Story 1: "Core Transaction Processing Pipeline" (Infrastructure Services)
**Tasks:** TASK-016, TASK-017, TASK-018, TASK-019, TASK-020
**Duration:** ~1 hour
**Value:** Complete the S3-to-Transaction parsing pipeline

**Acceptance Criteria:**
- Transaction files can be parsed from S3
- Metadata extraction working
- Content-type detection operational
- Audit service writing entries
- Structured logging configured

**Deployment:** Backend API update only

---

### Story 2: "Authentication & Authorization" (Auth Use Cases + API)
**Tasks:** TASK-021, TASK-028
**Duration:** ~45 minutes
**Value:** Complete authentication flow with lockout and token refresh

**Acceptance Criteria:**
- Login endpoint operational
- JWT tokens issued
- Refresh token rotation working
- Account lockout enforced
- Logout invalidates tokens

**Deployment:** Backend API update

---

### Story 3: "Data Access Layer" (Transaction & Profile Use Cases + API)
**Tasks:** TASK-022, TASK-023, TASK-029, TASK-030
**Duration:** ~1 hour
**Value:** Complete credential profile and transaction retrieval

**Acceptance Criteria:**
- Credential profiles CRUD operational
- S3 connection testing working
- Transaction search working
- Transaction detail retrieval working
- All operations audited

**Deployment:** Backend API update

---

### Story 4: "User Management" (Preferences, Settings, Audit)
**Tasks:** TASK-024, TASK-031
**Duration:** ~30 minutes
**Value:** Complete user preferences and admin settings

**Acceptance Criteria:**
- User preferences can be saved
- Admin settings can be updated
- Audit log query endpoint operational

**Deployment:** Backend API update

---

### Story 5: "Frontend Foundation" (React Setup + Redux)
**Tasks:** TASK-032, TASK-033, TASK-034
**Duration:** ~1.5 hours
**Value:** Frontend project with state management and API integration

**Acceptance Criteria:**
- Vite project building successfully
- Redux store configured
- RTK Query API endpoints defined
- TypeScript strict mode passing
- Auth state management working

**Deployment:** Frontend + Backend

---

### Story 6: "Authentication UI"
**Tasks:** TASK-035 (partial), TASK-036
**Duration:** ~45 minutes
**Value:** Users can log in via web UI

**Acceptance Criteria:**
- Login page functional
- Protected routes working
- JWT tokens stored
- Error messages displayed
- Redirects working

**Deployment:** Frontend + Backend

---

### Story 7: "Transaction Search & View"
**Tasks:** TASK-035 (partial), TASK-037, TASK-038, TASK-039, TASK-040
**Duration:** ~2 hours
**Value:** Core transaction browsing functionality

**Acceptance Criteria:**
- Transaction search working
- Filter bar operational
- Transaction list displays results
- Detail page shows full transaction
- Request/response headers and bodies displayed
- Syntax highlighting for JSON/XML

**Deployment:** Frontend + Backend

---

### Story 8: "Admin Features"
**Tasks:** TASK-041, TASK-042, TASK-043
**Duration:** ~1.5 hours
**Value:** Complete admin and user management UI

**Acceptance Criteria:**
- Credential profiles CRUD UI working
- User preferences page operational
- Admin settings page working
- Audit log viewer functional
- Navigation and routing complete

**Deployment:** Frontend + Backend

---

### Story 9: "Production Deployment"
**Tasks:** TASK-044
**Duration:** ~30 minutes
**Value:** Docker containerization for local deployment

**Acceptance Criteria:**
- API Dockerfile builds
- Frontend Dockerfile builds
- Docker Compose starts all services
- Environment variables configured
- Health checks operational

**Deployment:** Full stack via Docker Compose

---

## Execution Plan

### Phase 1: Backend API POC (Immediate)
**Goal:** Get to a working, testable backend API
**Stop After:** TASK-031
**Duration:** ~2.5 hours from current position

**Tasks Remaining:**
- TASK-016: Transaction Parser ← **Currently in progress**
- TASK-017: Metadata Extractors
- TASK-018: Utility Services (Gzip, ContentType, Truncator)
- TASK-019: Audit Service
- TASK-020: Serilog Configuration
- TASK-021: Auth Use Cases
- TASK-022: Credential Profile Use Cases
- TASK-023: Transaction Use Cases
- TASK-024: Preferences & Settings Use Cases
- TASK-025: API Program.cs
- TASK-026: API Middleware
- TASK-027: Audit Action Filter
- TASK-028: Auth Controller
- TASK-029: Credential Profiles Controller
- TASK-030: Transactions Controller
- TASK-031: User Preferences, Settings, Audit Controllers

**Checkpoint Actions:**
1. Stop pipeline after TASK-031 completes
2. Run `dotnet build` on all projects
3. Run `dotnet test` on test project
4. Start API with `dotnet run`
5. Access Swagger at `http://localhost:8080/swagger`
6. Test core endpoints via Swagger UI

**Decision Point:**
- ✅ If POC works: Celebrate, document, decide on next story
- ❌ If issues found: Debug and fix before proceeding

---

### Phase 2: Choose Next Story
**Based on Priority:**

**Option 1: Quick Win → Story 9 (Docker)**
- Add Docker support to backend POC
- Deploy to local containers
- **Time:** 30 minutes
- **Value:** Production-ready backend deployment

**Option 2: Complete Feature Set → Stories 5-8 (Frontend)**
- Build complete UI on top of working API
- **Time:** ~5.5 hours
- **Value:** Full user experience

**Option 3: Incremental → Story 1 (Transaction Pipeline)**
- Fix/complete any S3 parsing issues from TASK-016
- **Time:** 1 hour
- **Value:** Ensure core data retrieval works

---

## Story Deployment Strategy

Each story after the POC follows this pattern:

### 1. Story Development
```bash
# Resume pipeline for specific story tasks
python3 run_story.py --story-id 1 --tasks TASK-016,TASK-017,TASK-018,TASK-019,TASK-020

# Monitor progress
python3 monitor_with_intervention.py
```

### 2. Story Testing
```bash
# Run tests for story
cd src/DataViewer.{Layer}
dotnet test --filter "Category=Story1"

# Or frontend tests
cd frontend
npm test -- --grep "Story1"
```

### 3. Story Deployment
```bash
# Backend stories
dotnet build && dotnet run

# Frontend stories
npm run dev

# Docker stories
docker-compose up --build
```

### 4. Story Acceptance
- Run acceptance tests
- Verify functionality manually
- Document any issues
- Mark story complete

---

## Docker Deployment (After TASK-044)

### Local Development Stack
```yaml
# docker-compose.yml
services:
  db:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: dataviewer
      POSTGRES_USER: dataviewer
      POSTGRES_PASSWORD: dev_password
    ports:
      - "5432:5432"
    volumes:
      - pgdata:/var/lib/postgresql/data

  api:
    build: ./src/DataViewer.API
    environment:
      DATAVIEWER_DB_CONNECTION: "Host=db;Database=dataviewer;Username=dataviewer;Password=dev_password"
      DATAVIEWER_ENCRYPTION_KEY: "{32-byte-base64-key}"
      JWT__SECRET: "{32-byte-secret}"
    ports:
      - "8080:8080"
    depends_on:
      - db

  frontend:
    build: ./frontend
    ports:
      - "3000:80"
    depends_on:
      - api
```

### Start Everything
```bash
# Copy example env file
cp .env.local.example .env.local

# Generate secrets
python3 generate_secrets.py > .env.local

# Start stack
docker-compose up --build

# Access application
open http://localhost:3000
```

---

## Progress Tracking

### Current Status
```
[=========>................] 34% (15/44 tasks completed)

Completed:
✅ TASK-001: Solution structure
✅ TASK-002: Domain entities
✅ TASK-003: Enums and value objects
✅ TASK-004: Domain exceptions
✅ TASK-005: Application interfaces
✅ TASK-006: DTOs
✅ TASK-007: Encryption service
✅ TASK-008: EF Core DbContext
✅ TASK-009: Database provider factory
✅ TASK-010: User repository
✅ TASK-011: Credential profile repository
✅ TASK-012: Audit repository
✅ TASK-013: Settings repositories
✅ TASK-014: Token service
✅ TASK-015: S3 service

In Progress:
🔄 TASK-016: Transaction parser (test stage)

Pending: 28 tasks
```

### Estimated Completion Times

**To Backend API POC (TASK-031):**
- Current: TASK-016 in progress
- Remaining: 15 tasks
- Time per task: ~8-10 minutes average
- **Estimated:** 2-2.5 hours

**To Full Application (TASK-043):**
- Remaining: 27 tasks
- **Estimated:** 5-6 hours

**To Production-Ready (TASK-044):**
- Remaining: 28 tasks
- **Estimated:** 6 hours

---

## Recommendations

### Immediate Next Steps:
1. **Let TASK-016 complete** (transaction parser currently running)
2. **Continue pipeline through TASK-031** (Backend API POC)
3. **Stop and test at TASK-031 checkpoint**
4. **Verify backend API works** via Swagger

### After POC:
1. **Quick Docker Win** - Run Story 9 (TASK-044) to containerize backend POC
2. **Deploy to Local Docker** - Test full deployment
3. **Then Build Frontend** - Stories 5-8 for complete UI
4. **Final Integration** - Full stack testing

### Alternative Approach:
1. **Stop pipeline now** after TASK-016 completes
2. **Manually test S3 integration** with real credentials
3. **Fix any issues** before proceeding
4. **Resume pipeline** with confidence

---

## Testing Each Milestone

### Backend API POC Testing (After TASK-031):
```bash
# 1. Database
dotnet ef database update
psql -d dataviewer -c "\dt"

# 2. API startup
dotnet run --project src/DataViewer.API

# 3. Health checks
curl http://localhost:8080/health
curl http://localhost:8080/health/ready

# 4. Swagger UI
open http://localhost:8080/swagger

# 5. Authentication
curl -X POST http://localhost:8080/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"admin123"}'

# 6. Protected endpoint
curl http://localhost:8080/api/v1/transactions \
  -H "Authorization: Bearer {token}"
```

### Full Application Testing (After TASK-043):
```bash
# 1. Start backend
cd src/DataViewer.API && dotnet run

# 2. Start frontend
cd frontend && npm run dev

# 3. Open browser
open http://localhost:5173

# 4. Test user flows:
# - Login
# - Search transactions
# - View transaction detail
# - Manage credential profiles (admin)
# - Update preferences
```

### Docker Deployment Testing (After TASK-044):
```bash
# 1. Build and start
docker-compose up --build

# 2. Check all containers running
docker-compose ps

# 3. Check logs
docker-compose logs -f api

# 4. Access application
open http://localhost:3000

# 5. Verify persistence
docker-compose down
docker-compose up
# Data should persist
```

---

## Summary

**Recommended POC:** Backend API (stop after TASK-031)
**Time to POC:** ~2.5 hours from now
**Testing:** Swagger UI + curl commands
**Deployment:** Docker available with +30 min (TASK-044)

**Story Approach:** 9 stories, each deployable independently
**Story Duration:** 30 min - 2 hours each
**Total Time:** ~6 hours for complete system

**Next Checkpoint:** TASK-031 completion → Test backend API → Decide next story
