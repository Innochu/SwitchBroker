# Multi-stage Dockerfile for building and running the SwitchBroker ASP.NET Core app
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy the project file and restore dependencies
COPY SwitchBroker/*.csproj ./SwitchBroker/
RUN dotnet restore SwitchBroker/SwitchBroker.csproj

# Copy the rest of the source and publish
COPY . .
WORKDIR /src/SwitchBroker
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80
ENTRYPOINT ["dotnet", "SwitchBroker.dll"]
