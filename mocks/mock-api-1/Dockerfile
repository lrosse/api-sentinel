FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

COPY ["mocks/mock-api-1/MockApi1.csproj", "mocks/mock-api-1/"]
RUN dotnet restore "mocks/mock-api-1/MockApi1.csproj"

COPY . .
RUN dotnet publish "mocks/mock-api-1/MockApi1.csproj" \
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
ENTRYPOINT ["dotnet", "MockApi1.dll"]
