# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached between builds.
COPY Chandoo.csproj ./
RUN dotnet restore Chandoo.csproj

COPY . .
RUN dotnet publish Chandoo.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Forwarded headers are trusted because the site is only reachable through the reverse proxy (see docker-compose.yml),
# so canonical links and the sitemap use the visitor's https address.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    TZ=Asia/Tehran \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true

COPY --from=build /app/publish .

# Nothing is written to disk, so the image's unprivileged user is enough.
USER app

EXPOSE 8080
ENTRYPOINT ["dotnet", "Chandoo.dll"]
