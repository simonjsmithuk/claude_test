#!/bin/bash

# DataViewer Backend Startup Script
# Runs the .NET backend API locally with required environment variables

cd src/DataViewer.API

# Set environment variables
export ASPNETCORE_ENVIRONMENT=Development
export DATAVIEWER_ENCRYPTION_KEY="BjlDykspAVZ+/WayREzbGZ7ebMIMC/b+1mO/PzT9FIs="
export JWT__SECRET="VrAXH7uEdlW28IY3MMyVPTld3+Ggn6dSO/S9ts7pnnY="
export ConnectionStrings__DefaultConnection="Host=winhost;Database=dataviewer;Username=dview;Password=dview01"
export DatabaseProvider=postgresql

# Run the backend
echo "Starting DataViewer API on http://localhost:8080"
dotnet run --urls "http://localhost:8080"
