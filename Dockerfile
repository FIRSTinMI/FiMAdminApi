# TODO: Move these images back to stable once .NET 9 is released

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["FiMAdminApi/FiMAdminApi.csproj", "FiMAdminApi/"]
COPY ["nuget.config", "."]
RUN dotnet restore "FiMAdminApi/FiMAdminApi.csproj"
COPY . .
WORKDIR "/src/FiMAdminApi"
RUN dotnet build "FiMAdminApi.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "FiMAdminApi.csproj" -c $BUILD_CONFIGURATION -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
# There's some sort of weirdness in .NET where adding appsettings.json hangs for a while if reloadOnChange is true
# This is having significant impact on cold start performance in the cloud 
# We should never be changing this file in prod, so we can safely set it to false
ENV DOTNET_hostBuilder__reloadConfigOnChange=false
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FiMAdminApi.dll"]
