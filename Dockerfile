FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json ./
COPY src/ ./src/
RUN dotnet publish src/apps/webapi/ModularMonolith.WebApi.csproj -c Release -o /out/webapi /p:UseAppHost=false \
    && dotnet publish src/apps/seed/ModularMonolith.Seed.csproj -c Release -o /out/seed /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS seed
WORKDIR /app
COPY --from=build /out/seed ./
USER $APP_UID
ENTRYPOINT ["dotnet", "ModularMonolith.Seed.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS webapi
WORKDIR /app
COPY --from=build /out/webapi ./
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "ModularMonolith.WebApi.dll"]
