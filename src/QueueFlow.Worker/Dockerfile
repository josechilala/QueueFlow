FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/QueueFlow.Worker/QueueFlow.Worker.csproj
RUN dotnet publish src/QueueFlow.Worker/QueueFlow.Worker.csproj --no-restore -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "QueueFlow.Worker.dll"]
