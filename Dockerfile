FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json ./
COPY src/ ./src/
RUN dotnet publish src/apps/webapi/ModularMonolith.WebApi.csproj -c Release -o /out/webapi /p:UseAppHost=false \
    && dotnet publish src/apps/seed/ModularMonolith.Seed.csproj -c Release -o /out/seed /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS seed
WORKDIR /app
RUN mkdir -p /app/dpkeys && chown $APP_UID /app/dpkeys
COPY --from=build /out/seed ./
USER $APP_UID
ENTRYPOINT ["dotnet", "ModularMonolith.Seed.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS webapi
WORKDIR /app
RUN mkdir -p /app/dpkeys && chown $APP_UID /app/dpkeys
COPY --from=build /out/webapi ./
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "ModularMonolith.WebApi.dll"]

FROM node:24-alpine AS frontend-build
WORKDIR /source
COPY src/apps/web/package*.json ./
RUN npm ci
COPY src/apps/web/ ./
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.28-alpine AS frontend
COPY --from=frontend-build /source/dist /usr/share/nginx/html
COPY deploy/nginx/default.conf.template /etc/nginx/templates/default.conf.template
ENV NGINX_ENVSUBST_FILTER=API_UPSTREAM
EXPOSE 8443
