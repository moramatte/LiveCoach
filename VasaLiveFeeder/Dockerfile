# Multi-stage Dockerfile: build with .NET SDK, runtime with Playwright base image

# Stage 1: build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore. Adjust paths if your project layout differs.
COPY ["VasaLiveFeeder/VasaLiveFeeder.csproj", "VasaLiveFeeder/"]
RUN dotnet restore "VasaLiveFeeder/VasaLiveFeeder.csproj"

# Copy everything and publish
COPY . .
RUN dotnet publish "VasaLiveFeeder/VasaLiveFeeder.csproj" -c Release -o /app/publish --no-restore

# Stage 2: runtime with Playwright (includes browsers and deps)
FROM mcr.microsoft.com/playwright:focal
WORKDIR /app

# Copy published app
COPY --from=build /app/publish .

# Optional: set Playwright browsers path if you want a fixed location
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright

# Expose port (if your function uses HTTP; Azure Function uses its own entrypoint)
EXPOSE 80

ENTRYPOINT ["dotnet", "VasaLiveFeeder.dll"]
