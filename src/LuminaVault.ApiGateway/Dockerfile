FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/LuminaVault.ApiGateway/LuminaVault.ApiGateway.csproj", "src/LuminaVault.ApiGateway/"]
COPY ["src/LuminaVault.ServiceDefaults/LuminaVault.ServiceDefaults.csproj", "src/LuminaVault.ServiceDefaults/"]
COPY ["Directory.Packages.props", "."]
RUN dotnet restore "src/LuminaVault.ApiGateway/LuminaVault.ApiGateway.csproj"
COPY . .
WORKDIR "/src/src/LuminaVault.ApiGateway"
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "LuminaVault.ApiGateway.dll"]
