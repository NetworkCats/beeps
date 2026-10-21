# syntax=docker/dockerfile:1
# The production image (PLAN 9.3): client assets with Node, then the app and its migration bundle with the .NET SDK,
# run on the ASP.NET runtime. Built and pushed by .github/workflows/deploy.yml; `docker build .` builds the same.

FROM node:24.22.0-trixie-slim AS client
WORKDIR /src
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY tsconfig.json ./
COPY scripts/ scripts/
COPY src/Beeps/Client/ src/Beeps/Client/
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.402-noble AS build
ARG TARGETARCH
WORKDIR /src
# .editorconfig holds analyzer settings the build treats as errors without.
COPY .editorconfig global.json Directory.Build.props Directory.Packages.props dotnet-tools.json ./
COPY src/Beeps/Beeps.csproj src/Beeps/
RUN dotnet restore src/Beeps/Beeps.csproj -a "$TARGETARCH" && dotnet tool restore
COPY src/Beeps/ src/Beeps/
# MapStaticAssets fingerprints whatever is in wwwroot/dist when the app is built, so the assets go in first.
COPY --from=client /src/src/Beeps/wwwroot/dist/ src/Beeps/wwwroot/dist/
RUN dotnet publish src/Beeps/Beeps.csproj -c Release -a "$TARGETARCH" --no-restore -o /app -p:UseAppHost=false
# Migrations run once per deploy, before the new version takes traffic, never at each instance's start (PLAN 9.3).
# Making the bundle builds the DbContext, which wants a connection string; it is never opened here, and the bundle
# reads the real one from the environment when it runs.
RUN export ConnectionStrings__Postgres="Host=localhost;Database=beeps" \
    && case "$TARGETARCH" in amd64) rid=linux-x64 ;; arm64) rid=linux-arm64 ;; *) echo "No RID for $TARGETARCH" >&2; exit 1 ;; esac \
    && dotnet ef migrations bundle --project src/Beeps/Beeps.csproj --configuration Release --target-runtime "$rid" \
        --output /bundle/efbundle --force

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble
# curl for the health check; the deploy also asks /healthz before it switches nginx over.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
# Each instance keeps its downloaded reference data (IP locations, disposable email domains) here, on a volume, so a
# restart does not fetch them again.
RUN mkdir -p /var/lib/beeps && chown "$APP_UID" /var/lib/beeps
WORKDIR /app
COPY --from=build /app ./
COPY --from=build /bundle/efbundle ./efbundle
ENV ASPNETCORE_HTTP_PORTS=8080 \
    IpLocation__Directory=/var/lib/beeps/ip-location \
    DisposableEmail__Directory=/var/lib/beeps/disposable-email
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=5s --start-period=60s --retries=3 \
    CMD curl -fsS http://localhost:8080/healthz || exit 1
ENTRYPOINT ["dotnet", "Beeps.dll"]
