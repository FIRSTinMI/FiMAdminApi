# TODO: Move these images back to stable once .NET 9 is released

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Debug
WORKDIR /src
COPY ["FiMAdminApi/FiMAdminApi.csproj", "FiMAdminApi/"]
COPY ["nuget.config", "."]
RUN dotnet restore "FiMAdminApi/FiMAdminApi.csproj"
COPY . .
WORKDIR "/src/FiMAdminApi"
RUN dotnet build "FiMAdminApi.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Debug
RUN dotnet publish "FiMAdminApi.csproj" -c $BUILD_CONFIGURATION -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FiMAdminApi.dll"]
