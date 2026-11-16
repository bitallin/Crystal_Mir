FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/Crystal.Control.Api/Crystal.Control.Api.csproj src/Crystal.Control.Api/
COPY src/Crystal.Content/Crystal.Content.csproj src/Crystal.Content/
RUN dotnet restore src/Crystal.Control.Api/Crystal.Control.Api.csproj

COPY src/Crystal.Control.Api/ src/Crystal.Control.Api/
COPY src/Crystal.Content/ src/Crystal.Content/
RUN dotnet publish src/Crystal.Control.Api/Crystal.Control.Api.csproj \
    -c Release \
    -o /app/publish \
    --nologo \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /var/lib/crystal/releases /var/lib/crystal/assets \
    && chown -R app:app /app /var/lib/crystal

ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV DOTNET_NOLOGO=1
USER app
EXPOSE 8080

HEALTHCHECK --interval=10s --timeout=3s --retries=12 \
    CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080; printf "GET /health/ready HTTP/1.0\r\n\r\n" >&3; head -n 1 <&3 | grep -q "200"'

ENTRYPOINT ["dotnet", "Crystal.Control.Api.dll"]
