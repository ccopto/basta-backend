# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first for layer caching
COPY Basta.sln ./
COPY Basta.Server/Basta.Server.csproj Basta.Server/
COPY Basta.Server.Tests/Basta.Server.Tests.csproj Basta.Server.Tests/
RUN dotnet restore

# Copy everything else and build
COPY . .
WORKDIR /src/Basta.Server
RUN dotnet publish -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install curl for health check
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Create a non-root user for security
RUN useradd --create-home --shell /bin/false appuser

COPY --from=build /app/publish .

# Create data directory for SQLite and set ownership
RUN mkdir -p /app/data && chown -R appuser:appuser /app/data

USER appuser

EXPOSE 5000

ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:5000/api/health || exit 1

ENTRYPOINT ["dotnet", "Basta.Server.dll"]
