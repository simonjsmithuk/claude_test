# =============================================================================
# DataViewer — Multi-stage Dockerfile
# Target OS : Linux (Ubuntu-based mcr.microsoft.com images)
# Runtime   : .NET 8.0 ASP.NET Core + Node.js 20 LTS (frontend build only)
# Final user: non-root (app:app, UID 1001)
# =============================================================================

# ---------------------------------------------------------------------------
# Stage 1 — frontend build
#   Node.js 20 LTS; compiles the React/TypeScript SPA into static assets that
#   are later embedded in the ASP.NET publish output.
# ---------------------------------------------------------------------------
FROM node:20-alpine AS frontend-build

WORKDIR /frontend

# Copy package manifests first for layer-cache efficiency
COPY DataViewer.API/ClientApp/package*.json ./

RUN npm ci --prefer-offline

COPY DataViewer.API/ClientApp/ ./

# TypeScript strict-mode compile + production bundle (NFR-17, NFR-18)
RUN npm run build

# ---------------------------------------------------------------------------
# Stage 2 — .NET restore
#   Restore NuGet packages in a dedicated layer so the cache is invalidated
#   only when *.csproj / *.sln files change.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS restore

WORKDIR /solution

# Copy solution & project files
COPY DataViewer.sln ./
COPY DataViewer.Core/DataViewer.Core.csproj             DataViewer.Core/
COPY DataViewer.Infrastructure/DataViewer.Infrastructure.csproj DataViewer.Infrastructure/
COPY DataViewer.API/DataViewer.API.csproj               DataViewer.API/

# Optional: test projects
COPY DataViewer.Tests/DataViewer.Tests.csproj           DataViewer.Tests/ 2>/dev/null || true

RUN dotnet restore --locked-mode

# ---------------------------------------------------------------------------
# Stage 3 — build & test
#   Compiles the solution in Release configuration and runs the unit-test
#   suite with code coverage (NFR-13).
# ---------------------------------------------------------------------------
FROM restore AS build

WORKDIR /solution

# Copy full source
COPY DataViewer.Core/        DataViewer.Core/
COPY DataViewer.Infrastructure/ DataViewer.Infrastructure/
COPY DataViewer.API/         DataViewer.API/
COPY DataViewer.Tests/       DataViewer.Tests/ 2>/dev/null || true

# Build (no-restore because packages are already in the layer above)
RUN dotnet build --no-restore -c Release

# Run tests if project exists
RUN if [ -d "DataViewer.Tests" ]; then \
      dotnet test --no-build -c Release \
        --logger "trx;LogFileName=test-results.trx" \
        /p:CollectCoverage=true \
        /p:CoverletOutputFormat=opencover \
        /p:CoverletOutput=/coverage/; \
    fi

# ---------------------------------------------------------------------------
# Stage 4 — publish
#   Produces the self-contained, trimmed publish artefact plus copies in
#   the pre-built frontend static assets.
# ---------------------------------------------------------------------------
FROM build AS publish

RUN dotnet publish DataViewer.API/DataViewer.API.csproj \
      --no-restore \
      -c Release \
      -o /app/publish \
      /p:UseAppHost=false

# Inject the SPA build output into wwwroot (FR-30, OQ-12 — Kestrel-served SPA)
COPY --from=frontend-build /frontend/build /app/publish/wwwroot

# ---------------------------------------------------------------------------
# Stage 5 — runtime (final image)
#   Minimal ASP.NET 8 runtime layer.  No SDK, no build tooling.
#   Runs as a dedicated non-root user (UID 1001 / GID 1001).
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# Install curl (health-check probe)
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Create non-root user & group
RUN groupadd --gid 1001 app \
 && useradd  --uid 1001 --gid app --shell /bin/bash --create-home app

WORKDIR /app

# Copy published output
COPY --from=publish --chown=app:app /app/publish ./

# ASP.NET Core: log to stdout/stderr (containerised logging — NFR-09)
ENV ASPNETCORE_URLS="http://+:5000"
ENV ASPNETCORE_ENVIRONMENT="Production"
ENV DOTNET_RUNNING_IN_CONTAINER="true"
# Disable HTTPS redirect inside the container; TLS terminates at the
# reverse proxy / load balancer (OQ-09)
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED="true"

EXPOSE 5000

# Switch to non-root user
USER app

# Health check — polls /health every 30 s; 3 retries before unhealthy
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
  CMD curl -f http://localhost:5000/health || exit 1

ENTRYPOINT ["dotnet", "DataViewer.API.dll"]
