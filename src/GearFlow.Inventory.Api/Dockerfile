# Multi-stage. Contexto de build = raiz do repositório:
#   docker build -f src/GearFlow.Inventory.Api/Dockerfile -t gearflow-inventory-service .
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

# New Relic .NET agent: o PDF da Fase 4 exige reaproveitar a observabilidade da Fase 3.
# [trusted=yes] contorna rotação de chaves GPG do repo apt.newrelic.com (mesmo tratamento do gearflow-app).
RUN echo 'deb [trusted=yes] https://apt.newrelic.com/debian/ newrelic non-free' \
       | tee /etc/apt/sources.list.d/newrelic.list \
    && apt-get update \
    && apt-get install -y newrelic-dotnet-agent \
    && rm -rf /var/lib/apt/lists/*

ENV CORECLR_ENABLE_PROFILING=1 \
    CORECLR_PROFILER={36032161-FFC0-4B61-B559-F6C5D41BAE5A} \
    CORECLR_NEWRELIC_HOME=/usr/local/newrelic-dotnet-agent \
    CORECLR_PROFILER_PATH=/usr/local/newrelic-dotnet-agent/libNewRelicProfiler.so \
    NEW_RELIC_APP_NAME=GearFlow.Inventory.Api \
    NEW_RELIC_DISTRIBUTED_TRACING_ENABLED=true \
    NEW_RELIC_APPLICATION_LOGGING_ENABLED=true \
    NEW_RELIC_APPLICATION_LOGGING_FORWARDING_ENABLED=true \
    NEW_RELIC_APPLICATION_LOGGING_LOCAL_DECORATING_ENABLED=true \
    NEW_RELIC_LOG_LEVEL=info

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Build.props", "Directory.Packages.props", "./"]
COPY src/ src/
RUN dotnet restore "src/GearFlow.Inventory.Api/GearFlow.Inventory.Api.csproj"
RUN dotnet publish "src/GearFlow.Inventory.Api/GearFlow.Inventory.Api.csproj" \
    -c $BUILD_CONFIGURATION -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "GearFlow.Inventory.Api.dll"]
