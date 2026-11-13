# syntax=docker/dockerfile:1
# The Blazor host (src/PgRestWms.Web). Build context: the repository root.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG CONFIGURATION=Release
# The Husky target in the .csproj must not run in the image (no .git, no local tools).
ENV HUSKY=0
WORKDIR /src

# 1) Only what restore needs - this layer stays cached until dependencies change.
COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY src/PgRestWms.Web/PgRestWms.Web.csproj src/PgRestWms.Web/
RUN dotnet restore src/PgRestWms.Web/PgRestWms.Web.csproj

# 2) Sources and publish.
COPY src/ src/
RUN dotnet publish src/PgRestWms.Web/PgRestWms.Web.csproj \
    --configuration $CONFIGURATION \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false \
    /p:SkipTailwind=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# curl only for the HEALTHCHECK.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build --chown=$APP_UID /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

USER $APP_UID

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD curl --fail --silent http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "PgRestWms.Web.dll"]
