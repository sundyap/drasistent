# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY drAsistent.sln ./
COPY src/DrAsistent.Core/DrAsistent.Core.csproj src/DrAsistent.Core/
COPY src/DrAsistent.Solver/DrAsistent.Solver.csproj src/DrAsistent.Solver/
COPY src/DrAsistent.Data/DrAsistent.Data.csproj src/DrAsistent.Data/
COPY src/DrAsistent.Export/DrAsistent.Export.csproj src/DrAsistent.Export/
COPY src/DrAsistent.Web/DrAsistent.Web.csproj src/DrAsistent.Web/
RUN dotnet restore src/DrAsistent.Web/DrAsistent.Web.csproj -r linux-x64
COPY src/ src/
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
