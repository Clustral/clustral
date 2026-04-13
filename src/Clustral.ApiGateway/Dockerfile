# Build context: repo root
#   docker build -f src/Clustral.ApiGateway/Dockerfile -t clustral-api-gateway .

# ── Stage 1: restore + build ─────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG VERSION=0.0.0-dev
WORKDIR /src

COPY Directory.Packages.props    Directory.Packages.props
COPY packages/proto/              packages/proto/
COPY packages/Clustral.Sdk/      packages/Clustral.Sdk/
COPY packages/Clustral.Contracts/ packages/Clustral.Contracts/
COPY src/Clustral.ApiGateway/    src/Clustral.ApiGateway/

RUN dotnet publish src/Clustral.ApiGateway/Clustral.ApiGateway.csproj \
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

EXPOSE 8080
EXPOSE 5443

ENTRYPOINT ["dotnet", "Clustral.ApiGateway.dll"]
