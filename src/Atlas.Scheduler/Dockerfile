FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copy project files and restore
COPY ["src/Atlas.Core/Atlas.Core.csproj", "src/Atlas.Core/"]
COPY ["src/Atlas.Infrastructure/Atlas.Infrastructure.csproj", "src/Atlas.Infrastructure/"]
COPY ["src/Atlas.Scheduler/Atlas.Scheduler.csproj", "src/Atlas.Scheduler/"]
RUN dotnet restore "src/Atlas.Scheduler/Atlas.Scheduler.csproj"

# Copy the rest of the source code and publish
COPY . .
RUN dotnet publish "src/Atlas.Scheduler/Atlas.Scheduler.csproj" -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/out .
ENTRYPOINT ["dotnet", "Atlas.Scheduler.dll"]
