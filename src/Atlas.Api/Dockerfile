FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copy project files and restore
COPY ["src/Atlas.Core/Atlas.Core.csproj", "src/Atlas.Core/"]
COPY ["src/Atlas.Infrastructure/Atlas.Infrastructure.csproj", "src/Atlas.Infrastructure/"]
COPY ["src/Atlas.Api/Atlas.Api.csproj", "src/Atlas.Api/"]
RUN dotnet restore "src/Atlas.Api/Atlas.Api.csproj"

# Copy the rest of the source code and publish
COPY . .
RUN dotnet publish "src/Atlas.Api/Atlas.Api.csproj" -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/out .
EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "Atlas.Api.dll"]
