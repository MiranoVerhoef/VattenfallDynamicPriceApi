FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
# Install NativeAOT build prerequisites
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
       clang zlib1g-dev
ARG BUILD_CONFIGURATION=Release

WORKDIR "/src/VattenfallDynamicPriceApi"

COPY ["src/VattenfallDynamicPriceApi/VattenfallDynamicPriceApi.csproj", "/src/VattenfallDynamicPriceApi/"]
RUN dotnet restore "VattenfallDynamicPriceApi.csproj"

COPY ["src/VattenfallDynamicPriceApi/", "/src/VattenfallDynamicPriceApi/"]
RUN dotnet build "VattenfallDynamicPriceApi.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "VattenfallDynamicPriceApi.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime-deps:9.0-bookworm-slim
WORKDIR /app
COPY --from=publish /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["/app/VattenfallDynamicPriceApi"]
