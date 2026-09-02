# Image for running SDK pipelines as a one-shot job.
#
# Steps run in this image's own process, so every toolchain the pipeline shells out to has to
# be present here. That is now Node alone — to install, compile and publish. Generation is
# no longer a process: Kiota is a NuGet reference of SpaceCraft.Pipeline, restored into /app
# with everything else, so there is no CLI to install and nothing resolved off PATH.
#
# Node is still the exception, and it is why this image is large: npm, tsc and npm publish
# have no in-process equivalent. Its version is pinned below and restated in .nvmrc for a
# runner started on a developer's host, where npm comes off PATH instead.
#
# 11.0-preview, not 11.0: there is no plain 11.0 tag on mcr until GA on 10 November, and the
# -preview tags stop updating that day. Both need revisiting then.
ARG DOTNET_VERSION=11.0-preview
ARG NODE_VERSION=22

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Restore against the manifests alone so this layer survives source-only changes.
COPY Directory.Build.props Directory.Packages.props global.json nuget.config* ./
COPY src/SpaceCraft.Domain/SpaceCraft.Domain.csproj src/SpaceCraft.Domain/
COPY src/SpaceCraft.Infrastructure/SpaceCraft.Infrastructure.csproj src/SpaceCraft.Infrastructure/
COPY src/SpaceCraft.Pipeline/SpaceCraft.Pipeline.csproj src/SpaceCraft.Pipeline/
COPY src/SpaceCraft.Runner/SpaceCraft.Runner.csproj src/SpaceCraft.Runner/
RUN dotnet restore src/SpaceCraft.Runner/SpaceCraft.Runner.csproj

COPY src/ src/
RUN dotnet publish src/SpaceCraft.Runner/SpaceCraft.Runner.csproj \
        -c Release -o /app --no-restore

FROM node:${NODE_VERSION}-bookworm-slim AS node

# aspnet, not runtime: Data Protection and Serilog.AspNetCore both live in the
# Microsoft.AspNetCore.App shared framework, even though nothing here serves HTTP.
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final
ARG NODE_VERSION

# Node, taken from the official image rather than a distro package, so the version is the
# one we tested against.
# Npgsql probes for GSSAPI on every connection. Without it the probe fails to stderr, outside
# the logger, on every start — noise that would hide a real fault later.
RUN apt-get update \
 && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
 && rm -rf /var/lib/apt/lists/*

COPY --from=node /usr/local/bin/node /usr/local/bin/node
COPY --from=node /usr/local/lib/node_modules /usr/local/lib/node_modules
RUN ln -s /usr/local/lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm \
 && ln -s /usr/local/lib/node_modules/npm/bin/npx-cli.js /usr/local/bin/npx

COPY --from=build /app /app
WORKDIR /app

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

# npm and the generator both need a writable home; the workspace root is where runs unpack.
ENV HOME=/tmp \
    Pipeline__WorkspaceRoot=/tmp/spacecraft-workspaces

# Non-root, matching the posture the container sandbox uses for the steps themselves.
RUN useradd --uid 10001 --create-home --home-dir /home/runner runner \
 && mkdir -p /tmp/spacecraft-workspaces \
 && chown -R runner:runner /tmp/spacecraft-workspaces /app
USER 10001

ENTRYPOINT ["dotnet", "/app/SpaceCraft.Runner.dll"]
CMD ["--drain"]
