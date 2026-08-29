ARG VERSION=0.0.0-local
ARG VCS_REF=unknown

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG VERSION

WORKDIR /src

COPY ["BzsOIDC.sln", "."]
COPY ["src/BzsOIDC.AppHost.ServiceDefaults/BzsOIDC.AppHost.ServiceDefaults.csproj", "src/BzsOIDC.AppHost.ServiceDefaults/"]
COPY ["src/BzsOIDC.Idp.Client/BzsOIDC.Idp.Client.csproj", "src/BzsOIDC.Idp.Client/"]
COPY ["src/BzsOIDC.Idp/BzsOIDC.Idp.csproj", "src/BzsOIDC.Idp/"]
COPY ["src/Shared/BzsOIDC.Shared.Infrastructure/BzsOIDC.Shared.Infrastructure.csproj", "src/Shared/BzsOIDC.Shared.Infrastructure/"]

RUN dotnet restore "src/BzsOIDC.Idp/BzsOIDC.Idp.csproj"

COPY . .

WORKDIR /src
RUN dotnet publish "src/BzsOIDC.Idp/BzsOIDC.Idp.csproj" -c Release -o /app/publish /p:UseAppHost=false /p:Version="${VERSION}"

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
ARG VERSION
ARG VCS_REF

LABEL org.opencontainers.image.title="BzsOIDC IdP" \
      org.opencontainers.image.description="BzsOIDC identity provider" \
      org.opencontainers.image.source="https://github.com/Breezesea1/BzsOIDC" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${VCS_REF}"

WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "BzsOIDC.Idp.dll"]
