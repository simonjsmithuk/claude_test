# DataViewer - Docker Setup Guide

## ✅ Current Status

**Backend API**: ✅ Running in Docker at http://localhost:8080
**Frontend**: 🟡 Dockerfile created, needs build (can take 10+ minutes)

---

## Quick Start

### Start Backend Only (Fast - 5 seconds)
```bash
cd /home/simon/code_wsl/claude_test
docker-compose up -d api

# Verify
curl http://localhost:8080/health
# Expected: Healthy
```

### Start Both Backend + Frontend (Slow - 10+ minutes first time)
```bash
cd /home/simon/code_wsl/claude_test
docker-compose up -d

# This will:
# 1. Start backend API (fast)
# 2. Build frontend with npm install (slow - 10+ min)
# 3. Start frontend nginx (fast)
```

**URLs After Both Start**:
- Frontend UI: http://localhost:3000
- Backend API: http://localhost:8080
- Swagger: http://localhost:8080/swagger
- Health Check: http://localhost:8080/health

---

## Docker Compose Services

### Backend API (`api`)
- **Container**: `dataviewer-api`
- **Image**: Built from root `Dockerfile`
- **Port**: 8080 (host) → 8080 (container)
- **Tech**: .NET 8.0 ASP.NET Core Web API
- **Build Time**: ~30 seconds
- **Status**: ✅ Running

### Frontend (`frontend`)
- **Container**: `dataviewer-frontend`
- **Image**: Built from `frontend/Dockerfile`
- **Port**: 3000 (host) → 80 (container)
- **Tech**: React + Vite + Nginx
- **Build Time**: ~10+ minutes (npm install is slow)
- **Status**: 🟡 Ready to build, not started yet

---

## Architecture

```
┌────────────────────────────────────────┐
│  Browser                               │
│  http://localhost:3000                 │
└───────────────┬────────────────────────┘
                │
                │ HTTP
                ▼
┌────────────────────────────────────────┐
│  Frontend Container (Nginx)            │
│  - Serves React app                    │
│  - Proxies /api/* to backend           │
│  Port 3000:80                          │
└───────────────┬────────────────────────┘
                │
                │ /api/* → http://api:8080
                │ (Docker network)
                ▼
┌────────────────────────────────────────┐
│  Backend API Container                 │
│  - ASP.NET Core 8.0                    │
│  - 15 REST endpoints                   │
│  Port 8080:8080                        │
└───────────────┬────────────────────────┘
                │
                │ PostgreSQL
                ▼
┌────────────────────────────────────────┐
│  PostgreSQL Database (winhost)         │
│  - Not in Docker                       │
│  - Host: winhost                       │
│  - Database: dataviewer                │
└────────────────────────────────────────┘
```

---

## Frontend Dockerfile Explanation

### Multi-Stage Build
```dockerfile
# Stage 1: Build (node:20-alpine)
- npm ci (install dependencies)
- npm run build (Vite production build)
- Output: /app/dist

# Stage 2: Production (nginx:alpine)
- Copy built files to /usr/share/nginx/html
- Copy nginx.conf
- Serve on port 80
```

### nginx.conf
- Serves React app at `/`
- Proxies API requests to `http://api:8080/api/*`
- Proxies Swagger to `http://api:8080/swagger/*`
- SPA fallback: all routes serve `index.html`

---

## Why Frontend Build Is Slow

The frontend build includes:
1. **npm ci** - Downloads ~1000+ npm packages (5-8 minutes)
2. **Vite build** - Bundles React app (30-60 seconds)
3. **Layer caching** - Docker caches layers, so subsequent builds are faster

**First build**: 10+ minutes
**Subsequent builds**: 30-60 seconds (if package.json unchanged)

---

## Alternative: Run Frontend Locally (Faster)

Instead of Docker, you can run the frontend locally for development:

```bash
cd frontend

# Install dependencies (only needed once)
npm install

# Start dev server
npm run dev

# Access at http://localhost:3000
```

**Advantages**:
- Fast hot-reload during development
- Better debugging with source maps
- No Docker build time

**Disadvantage**:
- Requires Node.js installed locally

---

## Docker Commands

### View Running Containers
```bash
docker ps

# Expected output:
# CONTAINER ID   IMAGE              COMMAND                  STATUS         PORTS                    NAMES
# <id>           claude_test-api    "dotnet DataViewer.A…"  Up X minutes   0.0.0.0:8080->8080/tcp   dataviewer-api
# <id>           nginx:alpine       "nginx -g 'daemon of…"  Up X minutes   0.0.0.0:3000->80/tcp     dataviewer-frontend
```

### View Logs
```bash
# Backend logs
docker logs dataviewer-api -f

# Frontend logs
docker logs dataviewer-frontend -f

# Both
docker-compose logs -f
```

### Stop Containers
```bash
# Stop all
docker-compose down

# Stop one service
docker-compose stop api
docker-compose stop frontend
```

### Restart Containers
```bash
# Restart all
docker-compose restart

# Restart one
docker-compose restart api
```

### Rebuild Images
```bash
# Rebuild backend
docker-compose build api

# Rebuild frontend (slow!)
docker-compose build frontend

# Rebuild both
docker-compose build

# Force rebuild without cache
docker-compose build --no-cache
```

---

## Current Setup Summary

✅ **docker-compose.yml** - Defines 2 services (api, frontend)
✅ **Dockerfile** (root) - Backend API multi-stage build
✅ **frontend/Dockerfile** - Frontend multi-stage build (node + nginx)
✅ **frontend/nginx.conf** - Nginx configuration with API proxy
✅ **.env** - Environment variables (JWT_SECRET, ENCRYPTION_KEY)

---

## Recommended Approach

**For Development**:
```bash
# Backend in Docker
docker-compose up -d api

# Frontend locally (faster, better DX)
cd frontend
npm install
npm run dev
```

**For Production / Demo**:
```bash
# Both in Docker
docker-compose up -d

# Wait 10+ minutes for frontend build on first run
```

---

## Testing the Setup

### 1. Test Backend
```bash
# Health check
curl http://localhost:8080/health
# Expected: Healthy

# Swagger (if controllers are in image)
curl http://localhost:8080/swagger/v1/swagger.json | jq .info
```

### 2. Test Frontend (After Starting)
```bash
# Check container is running
docker ps | grep frontend

# Test nginx is serving
curl http://localhost:3000
# Expected: HTML content

# Test API proxy
curl http://localhost:3000/health
# Expected: Healthy (proxied to backend)
```

---

## Troubleshooting

### Backend won't start
```bash
# Check logs
docker logs dataviewer-api

# Common issues:
# - Missing .env file → create with JWT__SECRET and DATAVIEWER_ENCRYPTION_KEY
# - Database connection failed → check winhost is accessible
# - Port 8080 in use → stop other services or change port
```

### Frontend build fails
```bash
# Check logs
docker logs dataviewer-frontend

# Common issues:
# - npm install timeout → increase timeout or run locally
# - Build errors → check frontend/package.json is valid
# - Out of memory → increase Docker memory limit
```

### Can't access frontend
```bash
# Check container status
docker ps | grep frontend

# Check nginx logs
docker logs dataviewer-frontend

# Try accessing directly
curl http://localhost:3000
```

---

## Next Steps

1. **Start backend** (already done):
   ```bash
   docker-compose up -d api
   ```

2. **Build and start frontend** (optional, slow):
   ```bash
   docker-compose up -d frontend
   ```

   OR run frontend locally (faster):
   ```bash
   cd frontend
   npm install
   npm run dev
   ```

3. **Create test user** in database (see QUICK_START.md)

4. **Test the application**:
   - Backend: http://localhost:8080
   - Frontend: http://localhost:3000
   - Login with test credentials

---

**Current Status**: ✅ Backend running in Docker, Frontend ready to build
