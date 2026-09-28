# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached between builds.
COPY PortfolioManager.Api.csproj ./
RUN dotnet restore PortfolioManager.Api.csproj

COPY . .
RUN dotnet publish PortfolioManager.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    TZ=Asia/Tehran \
    ConnectionStrings__Default="Data Source=/app/data/portfolio.db"

# SQLite lives on the Liara disk mounted at /app/data (see liara.json).
# The mounted disk is root-owned, so the container runs as root to be able to write to it.
USER root
RUN mkdir -p /app/data

COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "PortfolioManager.Api.dll"]
