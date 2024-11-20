FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src

# Copy projects and restore
COPY PeakDNS.sln .
COPY PeakDNS/*.csproj PeakDNS/
COPY PeakDNS.test/*.csproj PeakDNS.test/
RUN dotnet restore

# Copy source code
COPY . .

WORKDIR /src/PeakDNS.test
RUN dotnet add package Microsoft.Extensions.Logging.Abstractions --version 6.0.0
WORKDIR /src

RUN mkdir -p PeakDNS.test/bin/Debug/net7.0/ && \
    cp PeakDNS.test/settings.ini PeakDNS.test/bin/Debug/net7.0/settings.ini

# Run tests
RUN dotnet test --no-restore --logger "trx;LogFileName=test-results.trx" || \
    (cat TestResults/test-results.trx && exit 1)

# Build main project
RUN dotnet publish PeakDNS/PeakDNS.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:7.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "PeakDNS.dll"]
