# TODO: Move these images back to stable once .NET 9 is released

FROM mcr.microsoft.com/dotnet/nightly/aspnet:9.0-preview AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/nightly/sdk:9.0-preview AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["FiMAdminApi/FiMAdminApi.csproj", "FiMAdminApi/"]
RUN dotnet restore "FiMAdminApi/FiMAdminApi.csproj"
COPY . .
WORKDIR "/src/FiMAdminApi"
RUN dotnet build "FiMAdminApi.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "FiMAdminApi.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FiMAdminApi.dll"]
