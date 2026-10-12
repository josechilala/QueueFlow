FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore QueueFlow.sln
RUN dotnet publish src/QueueFlow.Api/QueueFlow.Api.csproj --no-restore -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
USER root
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "QueueFlow.Api.dll"]
