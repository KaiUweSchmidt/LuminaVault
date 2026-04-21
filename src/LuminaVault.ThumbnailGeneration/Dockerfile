FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/LuminaVault.ThumbnailGeneration/LuminaVault.ThumbnailGeneration.csproj", "src/LuminaVault.ThumbnailGeneration/"]
COPY ["src/LuminaVault.ServiceDefaults/LuminaVault.ServiceDefaults.csproj", "src/LuminaVault.ServiceDefaults/"]
COPY ["Directory.Packages.props", "."]
RUN dotnet restore "src/LuminaVault.ThumbnailGeneration/LuminaVault.ThumbnailGeneration.csproj"
COPY . .
WORKDIR "/src/src/LuminaVault.ThumbnailGeneration"
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "LuminaVault.ThumbnailGeneration.dll"]
