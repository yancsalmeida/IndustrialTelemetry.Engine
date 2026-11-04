# ==============================================================================
# Stage 1: Build & Publish
# ==============================================================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files first for optimized layer caching
COPY ["src/IndustrialTelemetry.Domain/IndustrialTelemetry.Domain.csproj", "src/IndustrialTelemetry.Domain/"]
COPY ["src/IndustrialTelemetry.Application/IndustrialTelemetry.Application.csproj", "src/IndustrialTelemetry.Application/"]
COPY ["src/IndustrialTelemetry.Infrastructure/IndustrialTelemetry.Infrastructure.csproj", "src/IndustrialTelemetry.Infrastructure/"]
COPY ["src/IndustrialTelemetry.Api/IndustrialTelemetry.Api.csproj", "src/IndustrialTelemetry.Api/"]
COPY ["tests/IndustrialTelemetry.UnitTests/IndustrialTelemetry.UnitTests.csproj", "tests/IndustrialTelemetry.UnitTests/"]
COPY ["IndustrialTelemetry.sln", "./"]

RUN dotnet restore "IndustrialTelemetry.sln"

# Copy source code and build
COPY . .
WORKDIR "/src/src/IndustrialTelemetry.Api"
RUN dotnet publish "IndustrialTelemetry.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ==============================================================================
# Stage 2: Runtime Image
# ==============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Install curl for Docker container healthchecks
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
USER $APP_UID

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5100
EXPOSE 5100

HEALTHCHECK --interval=10s --timeout=5s --start-period=15s --retries=3 \
  CMD curl -f http://localhost:5100/health || exit 1

ENTRYPOINT ["dotnet", "IndustrialTelemetry.Api.dll"]
