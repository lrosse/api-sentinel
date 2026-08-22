FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

COPY ["mocks/mock-api-2/MockApi2.csproj", "mocks/mock-api-2/"]
RUN dotnet restore "mocks/mock-api-2/MockApi2.csproj"

COPY . .
RUN dotnet publish "mocks/mock-api-2/MockApi2.csproj" \
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
ENTRYPOINT ["dotnet", "MockApi2.dll"]
