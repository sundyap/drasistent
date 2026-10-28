# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore dupa copierea surselor: in .NET 10 scriptul Blazor (_framework/blazor.web.js) se adauga
# la restore doar daca proiectul contine deja componentele .razor
COPY src/ src/
RUN dotnet restore src/DrAsistent.Web/DrAsistent.Web.csproj -r linux-x64
RUN dotnet publish src/DrAsistent.Web/DrAsistent.Web.csproj -c Release -r linux-x64 --self-contained false --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
# fonturi pentru exportul PDF (diacritice)
RUN apt-get update \
    && apt-get install -y --no-install-recommends fontconfig fonts-dejavu-core wget \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    TZ=Europe/Bucharest
EXPOSE 8080
USER $APP_UID
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
    CMD wget -qO- http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "DrAsistent.Web.dll"]
