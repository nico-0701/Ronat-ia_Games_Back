# Imagem de produção da API (usada pelo Render). Build em duas etapas.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/

RUN dotnet restore src/RonatIa.Games.Api/RonatIa.Games.Api.csproj
RUN dotnet publish src/RonatIa.Games.Api/RonatIa.Games.Api.csproj \
    -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0

# A porta vem da variável PORT (definida pelo Render); sem ela, o padrão da imagem é 8080.
USER $APP_UID
ENTRYPOINT ["dotnet", "RonatIa.Games.Api.dll"]
