# Image for the API, sized for a serverless container platform.
#
# Unlike the runner, this carries no language toolchains: the API never generates or builds
# an SDK, it only records the intent and hands work to the queue. That keeps the image small,
# which is what a scale-to-zero platform charges for on every cold start.
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

# The platform injects PORT and routes to it; Program.cs binds 0.0.0.0 on that port. The
# default matches Scaleway's so the image also runs unchanged with plain `docker run`.
ENV PORT=8080 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1
EXPOSE 8080

# Non-root. The writable path is only needed because Data Protection wants somewhere to fall
# back to; in a real deployment the key ring is KMS-wrapped and shared, not local.
RUN useradd --uid 10001 --create-home --home-dir /home/api api \
 && chown -R api:api /app
USER 10001

ENTRYPOINT ["dotnet", "/app/SpaceCraft.Api.dll"]
