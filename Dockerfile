# =============================================================================
# DataViewer API - Multi-stage Docker Build
# =============================================================================
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

# Copy solution and project files
COPY DataViewer.sln ./
COPY src/DataViewer.Domain/DataViewer.Domain.csproj ./src/DataViewer.Domain/
COPY src/DataViewer.Application/DataViewer.Application.csproj ./src/DataViewer.Application/
COPY src/DataViewer.Infrastructure/DataViewer.Infrastructure.csproj ./src/DataViewer.Infrastructure/
COPY src/DataViewer.API/DataViewer.API.csproj ./src/DataViewer.API/

# Restore dependencies
RUN dotnet restore

# Copy source code
COPY src/ ./src/

# Build and publish
WORKDIR /source/src/DataViewer.API
RUN dotnet publish -c Release -o /app/publish

# =============================================================================
# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Copy published output from build stage
COPY --from=build /app/publish .

# Create logs directory
RUN mkdir -p /app/logs

# Expose port
EXPOSE 8080

# Set entry point
ENTRYPOINT ["dotnet", "DataViewer.API.dll"]
