FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/Crystal.Web.Gateway/Crystal.Web.Gateway.csproj src/Crystal.Web.Gateway/
RUN dotnet restore src/Crystal.Web.Gateway/Crystal.Web.Gateway.csproj

COPY src/Crystal.Web.Gateway/ src/Crystal.Web.Gateway/
RUN dotnet publish src/Crystal.Web.Gateway/Crystal.Web.Gateway.csproj \
    -c Release \
    -o /app/publish \
    --nologo \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5080
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV DOTNET_NOLOGO=1

USER app
EXPOSE 5080

HEALTHCHECK --interval=10s --timeout=3s --retries=12 \
    CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/5080; printf "GET /health/ready HTTP/1.0\r\n\r\n" >&3; head -n 1 <&3 | grep -q "200"'

ENTRYPOINT ["dotnet", "Crystal.Web.Gateway.dll"]
