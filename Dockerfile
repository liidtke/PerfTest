FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY PerfTest/PerfTest.csproj PerfTest/
RUN dotnet restore PerfTest/PerfTest.csproj

COPY PerfTest/ PerfTest/
RUN dotnet publish PerfTest/PerfTest.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

USER $APP_UID
ENTRYPOINT ["dotnet", "PerfTest.dll"]
