# DataViewer API - Deployment Guide

## Database Setup

You've already created the PostgreSQL database with these credentials:
- **Host:** winhost
- **Database:** dataviewer
- **Username:** dview
- **Password:** dview01

## Quick Start Options

### Option 1: Run with Docker Compose (Recommended)

1. **Generate secrets** (already done in `.env` file):
   ```bash
   # Environment variables are in .env file
   cat .env
   ```

2. **Apply database migrations**:
   ```bash
   cd src/DataViewer.Infrastructure
   dotnet ef database update --context AppDbContext --startup-project ../DataViewer.API
   ```

3. **Build and run with Docker**:
   ```bash
   docker-compose up --build
   ```

4. **Access the API**:
   - Swagger UI: http://localhost:8080/swagger
   - Health Check: http://localhost:8080/health
   - API Base: http://localhost:8080/api/v1

### Option 2: Run Locally with ./run-api.sh

```bash
./run-api.sh
```

This script will:
- Generate encryption keys
- Apply database migrations
- Start the API on http://localhost:5000

Access Swagger UI at: http://localhost:5000/swagger

### Option 3: Manual dotnet run

```bash
# Set environment variables
export ASPNETCORE_ENVIRONMENT=Development
export DATAVIEWER_ENCRYPTION_KEY=$(openssl rand -base64 32)
export JWT__SECRET=$(openssl rand -base64 32)

# Apply migrations
cd src/DataViewer.Infrastructure
dotnet ef database update --context AppDbContext --startup-project ../DataViewer.API
cd ..

# Run the API
cd DataViewer.API
dotnet run
```

## Environment Variables

Required environment variables:

| Variable | Description | Example |
|----------|-------------|---------|
| `ASPNETCORE_ENVIRONMENT` | Environment name | `Development` or `Production` |
| `DATAVIEWER_ENCRYPTION_KEY` | AES encryption key (32 bytes base64) | Generate with `openssl rand -base64 32` |
| `JWT__SECRET` | JWT signing secret (32 bytes base64) | Generate with `openssl rand -base64 32` |

Connection string is configured in `appsettings.Development.json`:
```
Host=winhost;Database=dataviewer;Username=dview;Password=dview01
```

## Initial Database Seed

The database will be seeded with:
- **Admin user**: username `admin`, password `admin123` (change after first login!)
- **System settings**: default values for JWT tokens, body size caps, lockout thresholds

## Testing the API

### Health Checks
```bash
# Basic health check
curl http://localhost:8080/health

# Readiness check
curl http://localhost:8080/health/ready
```

### Authentication

1. **Login** (get JWT token):
```bash
curl -X POST http://localhost:8080/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"admin123"}'
```

2. **Use the token** in subsequent requests:
```bash
curl http://localhost:8080/api/v1/transactions \
  -H "Authorization: Bearer YOUR_TOKEN_HERE"
```

### Swagger UI

Navigate to http://localhost:8080/swagger to:
- Explore all API endpoints
- Test endpoints interactively
- Authenticate with the "Authorize" button (enter: `Bearer YOUR_TOKEN`)

## Stopping the Application

### Docker Compose
```bash
docker-compose down
```

### Local run
Press `Ctrl+C` in the terminal

## Troubleshooting

### Port Already in Use
If port 8080 is in use, change it in `docker-compose.yml`:
```yaml
ports:
  - "9090:8080"  # Use port 9090 instead
```

### Database Connection Issues
1. Verify PostgreSQL is running on winhost
2. Test connection:
   ```bash
   psql -h winhost -U dview -d dataviewer
   ```
3. Check firewall rules allow connections to PostgreSQL

### Missing Migrations
If database tables don't exist:
```bash
cd src/DataViewer.Infrastructure
dotnet ef database update --context AppDbContext --startup-project ../DataViewer.API
```

## What's Deployed

✅ **Completed Components:**
- Complete Domain layer (entities, value objects, exceptions)
- Complete Application layer (all use cases)
- Complete Infrastructure layer (repositories, EF Core, services)
- API Program.cs with JWT auth, Swagger, CORS, health checks
- Database migrations
- Docker configuration

⚠️ **Not Yet Implemented:**
- API Controllers (TASK-028 through TASK-031) - API will start but have no endpoints yet
- API Middleware (TASK-026, TASK-027)
- Frontend (TASK-032 through TASK-043)

## Next Steps

To complete the backend API, implement:
1. **Controllers** (TASK-028-031):
   - AuthController (login, logout, refresh token)
   - CredentialProfilesController (CRUD for AWS profiles)
   - TransactionsController (search, view transactions)
   - AdminController (preferences, settings, audit logs)

2. **Middleware** (TASK-026-027):
   - Global exception handling
   - Audit action filter

The infrastructure is ready - controllers just need to wire up the use cases!
