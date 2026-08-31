# The development stand-in for Scaleway Key Manager.
#
# Deliberately outside src/: the API and runner images copy src/ wholesale, and a fake key
# manager has no business inside an image that talks to the real one.
ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Restore against the manifests alone so this layer survives source-only changes.
COPY Directory.Build.props Directory.Packages.props global.json nuget.config* ./
COPY infra/SpaceCraft.FakeScalewayKms/SpaceCraft.FakeScalewayKms.csproj infra/SpaceCraft.FakeScalewayKms/
RUN dotnet restore infra/SpaceCraft.FakeScalewayKms/SpaceCraft.FakeScalewayKms.csproj

COPY infra/SpaceCraft.FakeScalewayKms/ infra/SpaceCraft.FakeScalewayKms/
RUN dotnet publish infra/SpaceCraft.FakeScalewayKms/SpaceCraft.FakeScalewayKms.csproj \
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

# The key file lives here and must be on a volume: the key ring in Postgres is encrypted with
# it, so losing it strands every credential the database still holds.
RUN useradd --uid 10002 --create-home --home-dir /home/fakekms fakekms \
 && mkdir -p /data \
 && chown -R fakekms:fakekms /data /app
USER 10002

EXPOSE 8099
ENTRYPOINT ["dotnet", "/app/SpaceCraft.FakeScalewayKms.dll"]
