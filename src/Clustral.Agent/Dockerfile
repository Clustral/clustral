# Build context: repo root
#   docker build -f src/Clustral.Agent/Dockerfile -t clustral-agent .

# ── Stage 1: restore + build ──────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Copy everything needed for build.
COPY packages/proto/            packages/proto/
COPY packages/Clustral.Sdk/    packages/Clustral.Sdk/
COPY src/Clustral.Agent/       src/Clustral.Agent/

RUN dotnet publish src/Clustral.Agent/Clustral.Agent.csproj \
      -c Release \
      -o /app/publish

# ── Stage 2: runtime image ────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

# Run as non-root for defence-in-depth.
RUN groupadd --system --gid 1001 clustral \
 && useradd  --system --uid 1001 --gid clustral --no-create-home clustral

# Credential directory — mounted from a Secret in production.
RUN mkdir -p /etc/clustral && chown clustral:clustral /etc/clustral

COPY --from=build /app/publish .

USER clustral

# The agent does not expose any HTTP ports; all traffic is outbound to the
# ControlPlane gRPC endpoint.
ENTRYPOINT ["dotnet", "Clustral.Agent.dll"]
