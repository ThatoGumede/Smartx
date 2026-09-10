# syntax=docker/dockerfile:1
# Build context for this Dockerfile is the ./src directory (see docker-compose.yml),
# because SmartX.Api has a project reference to the sibling SmartX.Core library.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first so `dotnet restore` is cached independently of source changes.
COPY SmartX.Api/SmartX.Api.csproj SmartX.Api/
COPY SmartX.Core/SmartX.Core.csproj SmartX.Core/
RUN dotnet restore SmartX.Api/SmartX.Api.csproj

COPY SmartX.Api/ SmartX.Api/
COPY SmartX.Core/ SmartX.Core/
WORKDIR /src/SmartX.Api
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "SmartX.Api.dll"]
