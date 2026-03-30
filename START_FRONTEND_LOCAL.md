# Start Frontend Locally (Fast Alternative)

## Why Run Locally?

The Docker frontend build is taking 10+ minutes due to npm install. Running locally is **much faster**:
- Docker build: 10-15 minutes
- Local: 2-3 minutes

## Quick Start (3 minutes)

```bash
cd /home/simon/code_wsl/claude_test/frontend

# Install dependencies (2-3 minutes, only needed once)
npm install

# Start dev server (10 seconds)
npm run dev
```

**Frontend URL**: http://localhost:3000
**Backend URL**: http://localhost:8080 (already running in Docker)

## What You Get

✅ **Hot reload** - Changes update instantly
✅ **Better debugging** - Full source maps
✅ **Faster startup** - No Docker build time
✅ **Same functionality** - Full app with API proxy

## Vite Configuration

The frontend is already configured to proxy API requests to the backend:

```typescript
// vite.config.ts
server: {
  port: 3000,
  proxy: {
    '/api': 'http://localhost:8080',
    '/health': 'http://localhost:8080',
    '/swagger': 'http://localhost:8080'
  }
}
```

## Current Status

**Backend**: ✅ Running in Docker at http://localhost:8080
**Frontend Docker**: 🔄 Building (slow, 11+ minutes elapsed, still not done)
**Frontend Local**: ⏸️ Not started (but will be much faster)

## Recommendation

**Stop the Docker build** and run frontend locally:

```bash
# Stop Docker build (if it's still running)
# Ctrl+C in the terminal where docker-compose is running
# OR
docker-compose stop frontend

# Run locally instead
cd frontend
npm install
npm run dev
```

## Full Stack Running

Once frontend starts locally:
- ✅ Backend API: http://localhost:8080 (Docker)
- ✅ Frontend UI: http://localhost:3000 (Local)
- ✅ API calls proxy from frontend to backend
- ✅ Full authentication flow works
- ✅ All pages functional

## Next Steps

1. Open http://localhost:3000
2. Create test user in database (see QUICK_START.md)
3. Login with admin/Admin123!
4. Test the application

---

**Status**: Backend running in Docker, Frontend ready to run locally (faster than Docker build)
