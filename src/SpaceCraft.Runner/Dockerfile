# Image for running SDK pipelines as a one-shot job.
#
# In SandboxMode.PlatformIsolated there are no nested containers, so every toolchain the
# pipeline shells out to has to be present here: the Kiota CLI to generate, and Node to
# install, compile and publish. That is what makes this image large — and it is the trade
# for being able to run where a Docker daemon is not available.
#
# The Kiota version is pinned to match the runtime packages it emits code against. Bumping
# one without the other produces SDKs that do not compile; see PipelineOptions.KiotaImage.
ARG DOTNET_VERSION=10.0
ARG KIOTA_VERSION=1.34.1
ARG NODE_VERSION=22

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG KIOTA_VERSION
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

# Kiota ships as a dotnet tool; install it here so the runtime image needs no SDK.
RUN dotnet tool install --tool-path /kiota Microsoft.OpenApi.Kiota --version ${KIOTA_VERSION}

FROM node:${NODE_VERSION}-bookworm-slim AS node

# aspnet, not runtime: Data Protection and Serilog.AspNetCore both live in the
# Microsoft.AspNetCore.App shared framework, even though nothing here serves HTTP.
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final
ARG NODE_VERSION

# Node, taken from the official image rather than a distro package, so the version is the
# one we tested against.
COPY --from=node /usr/local/bin/node /usr/local/bin/node
COPY --from=node /usr/local/lib/node_modules /usr/local/lib/node_modules
RUN ln -s /usr/local/lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm \
 && ln -s /usr/local/lib/node_modules/npm/bin/npx-cli.js /usr/local/bin/npx

COPY --from=build /kiota /opt/kiota
ENV PATH="/opt/kiota:${PATH}"

COPY --from=build /app /app
WORKDIR /app

# The platform provides the boundary here; declaring it explicitly is what keeps
# PipelineOptions.Validate from refusing to start.
ENV Pipeline__Sandbox=PlatformIsolated \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
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
