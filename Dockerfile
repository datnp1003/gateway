FROM node:24-alpine AS dashboard
WORKDIR /src/dashboard
COPY dashboard/package*.json ./
RUN npm ci
COPY dashboard/ ./
RUN npm run build -- --outDir /dashboard

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Gateway.Domain/Gateway.Domain.csproj src/Gateway.Domain/
COPY src/Gateway.Application/Gateway.Application.csproj src/Gateway.Application/
COPY src/Gateway.Infrastructure/Gateway.Infrastructure.csproj src/Gateway.Infrastructure/
COPY src/Gateway/Gateway.csproj src/Gateway/
RUN dotnet restore src/Gateway/Gateway.csproj
COPY src/ src/
RUN dotnet publish src/Gateway/Gateway.csproj -c Release --no-restore -o /app
COPY --from=dashboard /dashboard/ /app/wwwroot/

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN mkdir -p /app/logs
COPY --from=build /app/ ./
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "Gateway.dll"]
