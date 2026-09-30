# syntax=docker/dockerfile:1

# ---- Build ----------------------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (cached layer as long as project files do not change).
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Leaderboard.Domain/Leaderboard.Domain.csproj src/Leaderboard.Domain/
COPY src/Leaderboard.Application/Leaderboard.Application.csproj src/Leaderboard.Application/
COPY src/Leaderboard.Infrastructure/Leaderboard.Infrastructure.csproj src/Leaderboard.Infrastructure/
COPY src/Leaderboard.Api/Leaderboard.Api.csproj src/Leaderboard.Api/
RUN dotnet restore src/Leaderboard.Api/Leaderboard.Api.csproj

COPY src/ src/
RUN dotnet publish src/Leaderboard.Api/Leaderboard.Api.csproj -c Release -o /app/publish --no-restore -p:UseAppHost=false \
    && mkdir -p /app/keys

# ---- Runtime --------------------------------------------------------------------------------------------------------
# Chiseled: no shell, no package manager, non-root by default (UID 1654). ~120 MB.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app

COPY --from=build --chown=1654:1654 /app/keys /app/keys
COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DataProtection__KeysPath=/app/keys
EXPOSE 8080
USER 1654

# No curl in chiseled images: the API binary probes itself.
HEALTHCHECK --interval=10s --timeout=5s --start-period=20s --retries=3 CMD ["dotnet", "Leaderboard.Api.dll", "healthcheck"]

ENTRYPOINT ["dotnet", "Leaderboard.Api.dll"]
