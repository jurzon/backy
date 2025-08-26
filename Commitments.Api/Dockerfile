# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

ENV DOTNET_NUGET_FALLBACK_PACKAGES="" \
    RestoreDisableParallel=true \
    NUGET_XMLDOC_MODE=skip \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_NOLOGO=1 \
    RestorePackagesPath=/root/.nuget/packages

COPY NuGet.config ./
COPY Directory.Build.props ./
COPY Backy.sln ./
COPY Commitments.Domain/Commitments.Domain.csproj Commitments.Domain/
COPY Commitments.Infrastructure/Commitments.Infrastructure.csproj Commitments.Infrastructure/
COPY Commitments.Api/Commitments.Api.csproj Commitments.Api/

RUN dotnet restore Commitments.Api/Commitments.Api.csproj --configfile NuGet.config

COPY . .
WORKDIR /src/Commitments.Api
RUN dotnet publish Commitments.Api.csproj -c Release -o /app/out --configfile ../NuGet.config

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/out .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Commitments.Api.dll"]