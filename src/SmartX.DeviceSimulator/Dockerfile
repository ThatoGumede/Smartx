# syntax=docker/dockerfile:1
# Build context is ./src (see docker-compose.yml).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY SmartX.DeviceSimulator/SmartX.DeviceSimulator.csproj SmartX.DeviceSimulator/
RUN dotnet restore SmartX.DeviceSimulator/SmartX.DeviceSimulator.csproj
COPY SmartX.DeviceSimulator/ SmartX.DeviceSimulator/
WORKDIR /src/SmartX.DeviceSimulator
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SmartX.DeviceSimulator.dll"]
