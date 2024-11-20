FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src

# Copy projects and restore
COPY GoodDns.sln .
COPY GoodDns/*.csproj GoodDns/
COPY GoodDns.test/*.csproj GoodDns.test/
RUN dotnet restore

# Copy source code
COPY . .

RUN mkdir -p GoodDns.test/bin/Debug/net7.0/ && \
    cp GoodDns.test/settings.ini GoodDns.test/bin/Debug/net7.0/settings.ini

# Run tests
RUN dotnet test --no-restore --logger "trx;LogFileName=test-results.trx" || \
    (cat TestResults/test-results.trx && exit 1)

# Build main project
RUN dotnet publish GoodDns/GoodDns.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:7.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "GoodDns.dll"]
