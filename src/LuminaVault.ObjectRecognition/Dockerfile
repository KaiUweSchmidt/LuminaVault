FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
WORKDIR "/src/src/LuminaVault.ObjectRecognition"
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
RUN mkdir -p /app/models
COPY models/yolov8m.onnx /app/models/yolov8m.onnx
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "LuminaVault.ObjectRecognition.dll"]
