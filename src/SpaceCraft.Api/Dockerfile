# Image for the API, run as a serverless container that never scales to zero: RunDispatcher
# lives in it and is the only thing that starts jobs (docs/deployment.md).
#
# Unlike the runner, this carries no language toolchains: the API never generates or builds
# an SDK, it only records the intent and hands work to the queue. That keeps the image small,
# which is what a deploy or a scale-out event pulls.
ARG DOTNET_VERSION=11.0-preview

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Restore against manifests alone so this layer survives source-only changes.
COPY Directory.Build.props Directory.Packages.props global.json nuget.config* ./
COPY src/SpaceCraft.Domain/SpaceCraft.Domain.csproj src/SpaceCraft.Domain/
COPY src/SpaceCraft.Infrastructure/SpaceCraft.Infrastructure.csproj src/SpaceCraft.Infrastructure/
COPY src/SpaceCraft.Pipeline/SpaceCraft.Pipeline.csproj src/SpaceCraft.Pipeline/
COPY src/SpaceCraft.Api/SpaceCraft.Api.csproj src/SpaceCraft.Api/
RUN dotnet restore src/SpaceCraft.Api/SpaceCraft.Api.csproj

COPY src/ src/
RUN dotnet publish src/SpaceCraft.Api/SpaceCraft.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final
WORKDIR /app
COPY --from=build /app .

# The base image listens on 8080, on every interface, through ASPNETCORE_HTTP_PORTS — the
# .NET 8+ convention, and all a startup probe connecting from outside the container needs.
# There is no PORT variable to read: a deployment that routes to a different port sets
# ASPNETCORE_HTTP_PORTS to that port, and the platform's own port declaration has to say the
# same number. See docs/deployment.md.
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1
EXPOSE 8080

# Non-root. The writable path is only needed because Data Protection wants somewhere to fall
# back to; in a real deployment the key ring is KMS-wrapped and shared, not local.
RUN useradd --uid 10001 --create-home --home-dir /home/api api \
 && chown -R api:api /app
USER 10001

ENTRYPOINT ["dotnet", "/app/SpaceCraft.Api.dll"]
