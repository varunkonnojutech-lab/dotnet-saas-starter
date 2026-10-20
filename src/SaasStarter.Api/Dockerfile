# Build context is the REPO ROOT (see docker-compose.yml) so the restore can
# see all three src projects.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/SaasStarter.Domain/SaasStarter.Domain.csproj src/SaasStarter.Domain/
COPY src/SaasStarter.Infrastructure/SaasStarter.Infrastructure.csproj src/SaasStarter.Infrastructure/
COPY src/SaasStarter.Api/SaasStarter.Api.csproj src/SaasStarter.Api/
RUN dotnet restore src/SaasStarter.Api/SaasStarter.Api.csproj

COPY src/ src/
RUN dotnet publish src/SaasStarter.Api/SaasStarter.Api.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
# SQLite lives here; docker-compose mounts a volume on /app/data.
ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__DefaultConnection="Data Source=/app/data/saas-starter.db"
COPY --from=build /app/publish .
RUN mkdir -p /app/data
EXPOSE 8080
ENTRYPOINT ["dotnet", "SaasStarter.Api.dll"]
