#!/bin/bash
# DataViewer API Startup Script
# Sets required environment variables and starts the API

# Set environment
export ASPNETCORE_ENVIRONMENT=Development

# Generate a random encryption key (32 bytes base64)
export DATAVIEWER_ENCRYPTION_KEY=$(openssl rand -base64 32)

# Generate a random JWT secret (32 bytes)
export JWT__SECRET=$(openssl rand -base64 32)

echo "Environment configured:"
echo "  ASPNETCORE_ENVIRONMENT: $ASPNETCORE_ENVIRONMENT"
echo "  DATAVIEWER_ENCRYPTION_KEY: [generated]"
echo "  JWT__SECRET: [generated]"
echo ""

# Apply migrations
echo "Applying database migrations..."
cd src/DataViewer.Infrastructure
dotnet ef database update --context AppDbContext --startup-project ../DataViewer.API
cd ../..

echo ""
echo "Starting DataViewer API..."
echo "Swagger UI will be available at: http://localhost:5000/swagger"
echo ""

# Run the API
cd src/DataViewer.API
dotnet run
