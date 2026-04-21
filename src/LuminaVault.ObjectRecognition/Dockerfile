FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/LuminaVault.ObjectRecognition/LuminaVault.ObjectRecognition.csproj", "src/LuminaVault.ObjectRecognition/"]
COPY ["src/LuminaVault.ServiceDefaults/LuminaVault.ServiceDefaults.csproj", "src/LuminaVault.ServiceDefaults/"]
COPY ["Directory.Packages.props", "."]
RUN dotnet restore "src/LuminaVault.ObjectRecognition/LuminaVault.ObjectRecognition.csproj"
COPY . .
WORKDIR "/src/src/LuminaVault.ObjectRecognition"
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
RUN mkdir -p /app/models && curl -fL -o /app/models/yolov12m-face.onnx "https://github.com/akanametov/yolo-face/releases/download/1.0.0/yolov12m-face.onnx"
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "LuminaVault.ObjectRecognition.dll"]
