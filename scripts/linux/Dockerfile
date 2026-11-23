# The workflow's Linux runner, near enough, for runs on the development machine (ROADMAP 26.1, scripts/linux-ci.mjs):
# Ubuntu 24.04 with the .NET SDK the repository's global.json asks for and Node 24, as .github/workflows/ci.yml sets
# them up; PHP, which the runner has; a user who is not root, as the runner's is; and no legacy time zone names, which
# the runner lacks. The SDK's image watches files by polling, which the runner does not; that is switched off. No
# database server: the integration tests run on SQLite only, and SQL Server only on GitHub.
FROM node:24-bookworm-slim AS node

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble
COPY --from=node /usr/local/bin/node /usr/local/bin/node
COPY --from=node /usr/local/lib/node_modules /usr/local/lib/node_modules
RUN ln -s ../lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm \
 && ln -s ../lib/node_modules/npm/bin/npx-cli.js /usr/local/bin/npx \
 && apt-get update \
 && apt-get install -y --no-install-recommends git rsync ca-certificates tzdata php-cli \
 && apt-get purge -y tzdata-legacy \
 && rm -rf /var/lib/apt/lists/* \
 && useradd --create-home --uid 1001 runner \
 && git config --system --add safe.directory '*'
ENV DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 NUGET_PACKAGES=/work/.nuget/packages CI=true \
    DOTNET_USE_POLLING_FILE_WATCHER=false
WORKDIR /work
