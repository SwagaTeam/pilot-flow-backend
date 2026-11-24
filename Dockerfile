FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS restore
WORKDIR /src

COPY . .
RUN dotnet restore PilotFlow.Backend.slnx

FROM restore AS test
RUN apk add --no-cache font-dejavu
ENTRYPOINT ["dotnet", "test", "PilotFlow.Backend.slnx", "--configuration", "Release", "--no-restore"]

FROM restore AS build-api
RUN dotnet publish src/PilotFlow.Api/PilotFlow.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM restore AS build-migrator
RUN dotnet publish src/PilotFlow.DbMigrator/PilotFlow.DbMigrator.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS migrator
WORKDIR /app
COPY --from=build-migrator /app/publish .
USER app
ENTRYPOINT ["dotnet", "PilotFlow.DbMigrator.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
RUN apk add --no-cache font-dejavu
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build-api /app/publish .
COPY docker/notices/PDFsharp-LICENSE.txt ./notices/PDFsharp-LICENSE.txt
USER app
ENTRYPOINT ["dotnet", "PilotFlow.Api.dll"]
