# GeoConquest: container image for Render (or any Docker host).
# Two stages: build with the full .NET 8 SDK, run on the small ASP.NET runtime image.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first so package downloads are cached between builds.
COPY ["Geo Conquest.csproj", "./"]
RUN dotnet restore "Geo Conquest.csproj"

COPY . .
RUN dotnet publish "Geo Conquest.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

# Run as the image's built-in non-root user (no root inside the container).
USER $APP_UID

# Render tells the app which port to use in $PORT (default 10000).
EXPOSE 10000
ENTRYPOINT ["sh", "-c", "exec dotnet \"Geo Conquest.dll\" --urls \"http://0.0.0.0:${PORT:-10000}\""]
