# Build context: repo root
#   docker build -f src/Clustral.ControlPlane/Dockerfile -t clustral-controlplane .

# ── Stage 1: restore + build ─────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG VERSION=0.0.0-dev
WORKDIR /src

# Copy Central Package Management props first (required for restore).
COPY Directory.Packages.props    Directory.Packages.props

# Copy everything needed for build.
COPY packages/proto/              packages/proto/
COPY packages/Clustral.Sdk/      packages/Clustral.Sdk/
COPY src/Clustral.ControlPlane/  src/Clustral.ControlPlane/

RUN dotnet publish src/Clustral.ControlPlane/Clustral.ControlPlane.csproj \
      -c Release \
      -p:Version=${VERSION} \
      -p:InformationalVersion=${VERSION} \
      -o /app/publish

# ── Stage 2: runtime image ───────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN groupadd --system --gid 1001 clustral \
 && useradd  --system --uid 1001 --gid clustral --no-create-home clustral

COPY --from=build /app/publish .

USER clustral

# REST on 5000, gRPC on 5001.
EXPOSE 5000 5001

ENTRYPOINT ["dotnet", "Clustral.ControlPlane.dll"]
