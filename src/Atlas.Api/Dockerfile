# --- Stage 1: Build React Dashboard ---
FROM node:20-alpine AS dashboard-build
WORKDIR /dashboard
COPY src/Atlas.Dashboard/package*.json ./
RUN npm install
COPY src/Atlas.Dashboard/ ./
RUN npm run build

# --- Stage 2: Build .NET API ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copy project files and restore
COPY ["src/Atlas.Core/Atlas.Core.csproj", "src/Atlas.Core/"]
COPY ["src/Atlas.Infrastructure/Atlas.Infrastructure.csproj", "src/Atlas.Infrastructure/"]
COPY ["src/Atlas.Api/Atlas.Api.csproj", "src/Atlas.Api/"]
RUN dotnet restore "src/Atlas.Api/Atlas.Api.csproj"

# Copy the rest of the source code
COPY . .

# Copy pre-built dashboard from Stage 1 into the Api's wwwroot directory
COPY --from=dashboard-build /src/Atlas.Api/wwwroot /app/src/Atlas.Api/wwwroot

# Publish the API
RUN dotnet publish "src/Atlas.Api/Atlas.Api.csproj" -c Release -o out

# --- Stage 3: Runtime ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/out .
EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "Atlas.Api.dll"]
