# Imagem de produção da API (usada pelo Render). Build em duas etapas.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# O BuildKit preenche com a arquitetura da imagem (amd64 no Render); o padrão cobre o build sem BuildKit.
ARG TARGETARCH=amd64
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/

# Restaurar e publicar para uma arquitetura só: sem isso o publish leva as bibliotecas nativas do SkiaSharp de todas as
# plataformas (~480 MB); só para linux-<arquitetura> a saída fica com ~27 MB.
RUN dotnet restore src/RonatIa.Games.Api/RonatIa.Games.Api.csproj -a $TARGETARCH --os linux
RUN dotnet publish src/RonatIa.Games.Api/RonatIa.Games.Api.csproj \
    -c Release -a $TARGETARCH --os linux --self-contained false -o /app --no-restore -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0

# A porta vem da variável PORT (definida pelo Render); sem ela, o padrão da imagem é 8080.
USER $APP_UID
ENTRYPOINT ["dotnet", "RonatIa.Games.Api.dll"]
