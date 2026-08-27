FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY tests/QueueFlow.LoadTests/QueueFlow.LoadTests.csproj tests/QueueFlow.LoadTests/
RUN dotnet restore tests/QueueFlow.LoadTests/QueueFlow.LoadTests.csproj
COPY tests/QueueFlow.LoadTests/ tests/QueueFlow.LoadTests/
RUN dotnet publish tests/QueueFlow.LoadTests/QueueFlow.LoadTests.csproj --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "QueueFlow.LoadTests.dll"]
