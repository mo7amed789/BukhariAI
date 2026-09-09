# ==============================================================================
# BukhariAI - Production Multi-Stage Dockerfile (.NET 10)
# ==============================================================================

# Stage 1: Base Runtime Environment
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

# Install Linux native dependencies for PDFium / DocLib font rendering
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        libfontconfig1 \
        libfreetype6 \
        ca-certificates \
        curl \
    && rm -rf /var/lib/apt/lists/*

# Stage 2: Build and Restore
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files for layer caching
COPY ["src/BukhariAI.Domain/BukhariAI.Domain.csproj", "src/BukhariAI.Domain/"]
COPY ["src/BukhariAI.Application/BukhariAI.Application.csproj", "src/BukhariAI.Application/"]
COPY ["src/BukhariAI.Infrastructure/BukhariAI.Infrastructure.csproj", "src/BukhariAI.Infrastructure/"]
COPY ["src/BukhariAI.Api/BukhariAI.Api.csproj", "src/BukhariAI.Api/"]

# Restore dependencies
RUN dotnet restore "src/BukhariAI.Api/BukhariAI.Api.csproj"

# Copy source tree and compile
COPY src/ src/
WORKDIR "/src/src/BukhariAI.Api"
RUN dotnet build "BukhariAI.Api.csproj" -c Release -o /app/build --no-restore

# Stage 3: Publish
FROM build AS publish
RUN dotnet publish "BukhariAI.Api.csproj" -c Release -o /app/publish --no-restore

# Stage 4: Production Image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Healthcheck to verify API responsiveness
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD curl --fail http://localhost:8080/ || exit 1

ENTRYPOINT ["dotnet", "BukhariAI.Api.dll"]
