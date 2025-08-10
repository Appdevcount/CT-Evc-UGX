
FROM globalcrep.azurecr.io/evicore-aspnet:7.0-kafka-alpine AS base

WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src

ARG PAT
RUN wget -qO- https://raw.githubusercontent.com/Microsoft/artifacts-credprovider/master/helpers/installcredprovider.sh | bash
ENV NUGET_CREDENTIALPROVIDER_SESSIONTOKENCACHE_ENABLED true
ENV VSS_NUGET_EXTERNAL_FEED_ENDPOINTS "{\"endpointCredentials\": [{\"endpoint\":\"https://pkgs.dev.azure.com/eviCoreDev/_packaging/eviCoreVSTSNugetFeed/nuget/v3/index.json\", \"password\":\"${PAT}\"}]}"

COPY ["RequestRouting.Api/RequestRouting.Api.csproj", "RequestRouting.Api/"]
RUN dotnet restore -s "https://api.nuget.org/v3/index.json" -s "https://pkgs.dev.azure.com/eviCoreDev/_packaging/eviCoreVSTSNugetFeed/nuget/v3/index.json" "RequestRouting.Api/RequestRouting.Api.csproj"

COPY . .
WORKDIR "RequestRouting.Api"
RUN dotnet build "RequestRouting.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "RequestRouting.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "RequestRouting.Api.dll"]
