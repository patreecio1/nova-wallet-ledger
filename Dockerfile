FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props NovaWalletLedger.sln ./
COPY src/BuildingBlocks/NovaWallet.BuildingBlocks.Domain/NovaWallet.BuildingBlocks.Domain.csproj src/BuildingBlocks/NovaWallet.BuildingBlocks.Domain/
COPY src/BuildingBlocks/NovaWallet.BuildingBlocks.Application/NovaWallet.BuildingBlocks.Application.csproj src/BuildingBlocks/NovaWallet.BuildingBlocks.Application/
COPY src/BuildingBlocks/NovaWallet.BuildingBlocks.Infrastructure/NovaWallet.BuildingBlocks.Infrastructure.csproj src/BuildingBlocks/NovaWallet.BuildingBlocks.Infrastructure/
COPY src/Modules/Wallet/NovaWallet.Wallet.Domain/NovaWallet.Wallet.Domain.csproj src/Modules/Wallet/NovaWallet.Wallet.Domain/
COPY src/Modules/Wallet/NovaWallet.Wallet.Application/NovaWallet.Wallet.Application.csproj src/Modules/Wallet/NovaWallet.Wallet.Application/
COPY src/Modules/Wallet/NovaWallet.Wallet.Infrastructure/NovaWallet.Wallet.Infrastructure.csproj src/Modules/Wallet/NovaWallet.Wallet.Infrastructure/
COPY src/Modules/Wallet/NovaWallet.Wallet.API/NovaWallet.Wallet.API.csproj src/Modules/Wallet/NovaWallet.Wallet.API/
COPY src/Api/NovaWallet.Api/NovaWallet.Api.csproj src/Api/NovaWallet.Api/
COPY tests/NovaWallet.Wallet.UnitTests/NovaWallet.Wallet.UnitTests.csproj tests/NovaWallet.Wallet.UnitTests/
COPY tests/NovaWallet.IntegrationTests/NovaWallet.IntegrationTests.csproj tests/NovaWallet.IntegrationTests/

RUN dotnet restore NovaWalletLedger.sln

COPY . .
RUN dotnet publish src/Api/NovaWallet.Api/NovaWallet.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app .
ENTRYPOINT ["dotnet", "NovaWallet.Api.dll"]
