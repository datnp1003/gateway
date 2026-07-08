FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Gateway/Gateway.csproj src/Gateway/
RUN dotnet restore src/Gateway/Gateway.csproj
COPY . .
RUN dotnet publish src/Gateway/Gateway.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN mkdir -p /app/logs
COPY --from=build /app .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "Gateway.dll"]
