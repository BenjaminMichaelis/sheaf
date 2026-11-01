# syntax=docker/dockerfile:1

# Build stage: full SDK image for compiling and publishing
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first for layer-cached restore.
# Changes to source code won't invalidate the restore layer.
COPY ["Sheaf/Sheaf.csproj", "Sheaf/"]
COPY ["Sheaf.Core/Sheaf.Core.csproj", "Sheaf.Core/"]
COPY ["Sheaf.Data/Sheaf.Data.csproj", "Sheaf.Data/"]
COPY ["Sheaf.ServiceDefaults/Sheaf.ServiceDefaults.csproj", "Sheaf.ServiceDefaults/"]
COPY ["Directory.Build.props", "."]
COPY ["Directory.Build.targets", "."]
COPY ["Directory.Packages.props", "."]
COPY ["NuGet.config", "."]
RUN --mount=type=cache,id=sheaf-nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet restore "Sheaf/Sheaf.csproj"

# Copy remaining source and publish in Release mode
COPY . .
RUN --mount=type=cache,id=sheaf-nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet publish "Sheaf/Sheaf.csproj" \
    -c Release \
    -o /app/publish

# Runtime stage: Ubuntu Noble Chiseled - distroless-like, minimal attack surface.
# No shell, no package manager, no root; runs as UID 65532 (app) by default.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true

# Copy published output from build stage
COPY --from=build /app/publish .

# Explicit non-root user. Chiseled images default to 'app' (UID 65532),
# but declaring it here makes the intent visible in the Dockerfile itself.
USER app

ENTRYPOINT ["dotnet", "Sheaf.dll"]
