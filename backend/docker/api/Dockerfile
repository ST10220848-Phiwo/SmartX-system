
# ---------------------------------------------------------------------------
# Build stage. Project files are copied before the source so that a change to a
# .cs file does not invalidate the restore layer — on a solution this size that
# is the difference between a 10-second rebuild and a 90-second one.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY global.json Directory.Build.props ./
COPY SmartX.sln ./
COPY src/SmartX.Core/SmartX.Core.csproj              src/SmartX.Core/
COPY src/SmartX.Contracts/SmartX.Contracts.csproj    src/SmartX.Contracts/
COPY src/SmartX.Ingest/SmartX.Ingest.csproj          src/SmartX.Ingest/
COPY src/SmartX.Api/SmartX.Api.csproj                src/SmartX.Api/
COPY tests/SmartX.Core.Tests/SmartX.Core.Tests.csproj tests/SmartX.Core.Tests/

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore SmartX.sln

COPY . .

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish src/SmartX.Api/SmartX.Api.csproj \
      -c $BUILD_CONFIGURATION \
      -o /app/publish \
      --no-restore \
      /p:UseAppHost=false

# ---------------------------------------------------------------------------
# Test stage. Built by the CI target only; `docker build --target test` runs the
# suite inside the same image the runtime is derived from.
# ---------------------------------------------------------------------------
FROM build AS test
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet test SmartX.sln -c Release --no-restore --logger "trx;LogFileName=results.trx"

# ---------------------------------------------------------------------------
# Runtime stage. aspnet, not sdk: the final image has no compiler in it.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
WORKDIR /app

# curl is the only addition to the base image, and it exists solely so the
# container can answer HEALTHCHECK without shelling out to the app itself.
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

# Non-root. The gateway is internet-facing in a real deployment.
RUN groupadd --system --gid 64123 smartx \
 && useradd  --system --uid 64123 --gid smartx --no-create-home smartx

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=1 \
    DOTNET_TieredPGO=1

COPY --from=build --chown=smartx:smartx /app/publish .

USER smartx
EXPOSE 8080

# Readiness, not liveness: the container is unhealthy when it can no longer absorb
# ingest traffic, which is what the orchestrator needs to know.
HEALTHCHECK --interval=15s --timeout=3s --start-period=20s --retries=3 \
  CMD curl --fail --silent http://localhost:8080/health/ready || exit 1

ENTRYPOINT ["dotnet", "SmartX.Api.dll"]
