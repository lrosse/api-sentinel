FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

COPY ["src/ApiSentinel.Api/ApiSentinel.Api.csproj", "src/ApiSentinel.Api/"]
COPY ["src/ApiSentinel.Infrastructure/ApiSentinel.Infrastructure.csproj", "src/ApiSentinel.Infrastructure/"]
COPY ["src/ApiSentinel.Modules.Identity/ApiSentinel.Modules.Identity.csproj", "src/ApiSentinel.Modules.Identity/"]
COPY ["src/ApiSentinel.Modules.ApiCatalog/ApiSentinel.Modules.ApiCatalog.csproj", "src/ApiSentinel.Modules.ApiCatalog/"]
COPY ["src/ApiSentinel.Modules.Monitoring/ApiSentinel.Modules.Monitoring.csproj", "src/ApiSentinel.Modules.Monitoring/"]
COPY ["src/ApiSentinel.Modules.Incidents/ApiSentinel.Modules.Incidents.csproj", "src/ApiSentinel.Modules.Incidents/"]
RUN dotnet restore "src/ApiSentinel.Api/ApiSentinel.Api.csproj"

COPY . .
RUN dotnet publish "src/ApiSentinel.Api/ApiSentinel.Api.csproj" \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "ApiSentinel.Api.dll"]
