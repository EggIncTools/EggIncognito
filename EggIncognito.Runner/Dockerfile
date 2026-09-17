# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:11.0.100-rc.1 AS build
WORKDIR /src
ENV NUGET_PACKAGES=/root/.nuget/packages

COPY nuget.config Directory.Build.props Directory.Build.targets Directory.Packages.props .editorconfig ./
COPY EggIncognito.Core/EggIncognito.Core.csproj EggIncognito.Core/
COPY EggIncognito.Data/EggIncognito.Data.csproj EggIncognito.Data/
COPY EggIncognito.Runner/EggIncognito.Runner.csproj EggIncognito.Runner/
ARG GITHUB_PACKAGES_USER
RUN --mount=type=secret,id=github_token \
    dotnet nuget update source github \
      --username "$GITHUB_PACKAGES_USER" \
      --password "$(cat /run/secrets/github_token)" \
      --store-password-in-clear-text \
      --configfile nuget.config \
    && dotnet restore EggIncognito.Runner/EggIncognito.Runner.csproj

COPY EggIncognito.Core/ EggIncognito.Core/
COPY EggIncognito.Data/ EggIncognito.Data/
COPY EggIncognito.Runner/ EggIncognito.Runner/

ARG GIT_SHA
ARG APP_VERSION
RUN set -eux; \
    STAMP=""; \
    [ -n "$GIT_SHA" ] && STAMP="$STAMP -p:SourceRevisionId=$GIT_SHA"; \
    [ -n "$APP_VERSION" ] && STAMP="$STAMP -p:MinVerVersionOverride=$APP_VERSION"; \
    dotnet publish EggIncognito.Runner/EggIncognito.Runner.csproj -c Release -o /app/publish $STAMP

FROM mcr.microsoft.com/dotnet/aspnet:11.0.0-rc.1 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends adb ideviceinstaller openssh-client \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .

ENV ADB_SERVER_SOCKET=tcp:127.0.0.1:5037

ENTRYPOINT ["dotnet", "EggIncognito.Runner.dll"]
