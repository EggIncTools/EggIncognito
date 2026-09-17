# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:11.0.100-rc.1 AS build
WORKDIR /src
ENV NUGET_PACKAGES=/root/.nuget/packages

COPY nuget.config Directory.Build.props Directory.Build.targets Directory.Packages.props .editorconfig ./
COPY EggIncognito.Core/EggIncognito.Core.csproj EggIncognito.Core/
COPY EggIncognito.Capture/EggIncognito.Capture.csproj EggIncognito.Capture/
COPY EggIncognito.Data/EggIncognito.Data.csproj EggIncognito.Data/
COPY EggIncognito.Bot/EggIncognito.Bot.csproj EggIncognito.Bot/
COPY EggIncognito.RouteGenerator/EggIncognito.RouteGenerator.csproj EggIncognito.RouteGenerator/
COPY EggIncognito.GameData/EggIncognito.GameData.csproj EggIncognito.GameData/
COPY EggIncognito.CssBuild/EggIncognito.CssBuild.csproj EggIncognito.CssBuild/
COPY EggIncognito/EggIncognito.csproj EggIncognito/
ARG GITHUB_PACKAGES_USER
RUN --mount=type=secret,id=github_token \
    dotnet nuget update source github \
      --username "$GITHUB_PACKAGES_USER" \
      --password "$(cat /run/secrets/github_token)" \
      --store-password-in-clear-text \
      --configfile nuget.config \
    && dotnet restore EggIncognito/EggIncognito.csproj \
    && dotnet restore EggIncognito.CssBuild/EggIncognito.CssBuild.csproj

COPY EggIncognito.Core/ EggIncognito.Core/
COPY EggIncognito.Capture/ EggIncognito.Capture/
COPY EggIncognito.Data/ EggIncognito.Data/
COPY EggIncognito.Bot/ EggIncognito.Bot/
COPY EggIncognito.RouteGenerator/ EggIncognito.RouteGenerator/
COPY EggIncognito.GameData/ EggIncognito.GameData/
COPY EggIncognito.CssBuild/ EggIncognito.CssBuild/
COPY EggIncognito/ EggIncognito/

ARG GIT_SHA
ARG APP_VERSION
RUN set -eux; \
    STAMP=""; \
    [ -n "$GIT_SHA" ] && STAMP="$STAMP -p:SourceRevisionId=$GIT_SHA"; \
    [ -n "$APP_VERSION" ] && STAMP="$STAMP -p:MinVerVersionOverride=$APP_VERSION"; \
    dotnet publish EggIncognito/EggIncognito.csproj -c Release -o /app/publish \
        -p:EmitTypes=false $STAMP; \
    test -s /app/publish/wwwroot/styles.css; \
    grep -q "btn-primary" /app/publish/wwwroot/styles.css; \
    test -s /app/publish/wwwroot/_framework/blazor.web.js

FROM mcr.microsoft.com/dotnet/aspnet:11.0.0-rc.1 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 iproute2 adb ideviceinstaller openssh-client \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
COPY EggIncognito/Endpoints /app/Endpoints
COPY EggIncognito/RouteMap /app/RouteMap
COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
RUN chmod +x /usr/local/bin/docker-entrypoint.sh

VOLUME ["/app/Endpoints"]

ARG GIT_SHA
ENV GIT_SHA=$GIT_SHA

ENV ASPNETCORE_URLS=http://+:8080
ENV EndpointsPath=/app/Endpoints
ENV NoBrowser=true

ENV ADB_SERVER_SOCKET=tcp:127.0.0.1:5037

EXPOSE 8080
EXPOSE 8443
ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
