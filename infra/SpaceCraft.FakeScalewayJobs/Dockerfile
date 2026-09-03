# The development stand-in for Scaleway Serverless Jobs.
#
# Deliberately outside src/: the API and runner images copy src/ wholesale, and a fake key
# manager has no business inside an image that talks to the real one.
ARG DOTNET_VERSION=11.0-preview

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Restore against the manifests alone so this layer survives source-only changes.
COPY Directory.Build.props Directory.Packages.props global.json nuget.config* ./
COPY infra/SpaceCraft.FakeScalewayJobs/SpaceCraft.FakeScalewayJobs.csproj infra/SpaceCraft.FakeScalewayJobs/
RUN dotnet restore infra/SpaceCraft.FakeScalewayJobs/SpaceCraft.FakeScalewayJobs.csproj

COPY infra/SpaceCraft.FakeScalewayJobs/ infra/SpaceCraft.FakeScalewayJobs/
RUN dotnet publish infra/SpaceCraft.FakeScalewayJobs/SpaceCraft.FakeScalewayJobs.csproj \
        -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final

# curl only so compose can wait for readiness the way it does for MinIO and Verdaccio; the
# aspnet image ships with neither curl nor wget.
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

COPY --from=build /app /app
WORKDIR /app

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

# The Docker CLI, because starting a job here means starting a container. Only the client —
# the daemon is the host's, reached through the socket compose mounts.
RUN apt-get update \
 && apt-get install -y --no-install-recommends docker.io \
 && rm -rf /var/lib/apt/lists/*

# Root, unlike its neighbour, and this is the honest cost of the design: talking to the host's
# Docker socket needs the socket's group, and pinning a gid that matches every developer's
# machine is not portable. Nothing here is reachable from a deployment — the API refuses a
# plaintext jobs endpoint outside Development.

EXPOSE 8098
ENTRYPOINT ["dotnet", "/app/SpaceCraft.FakeScalewayJobs.dll"]
