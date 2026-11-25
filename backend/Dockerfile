FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props FaizaGold.slnx ./
COPY src/FaizaGold.Domain/FaizaGold.Domain.csproj src/FaizaGold.Domain/
COPY src/FaizaGold.Api/FaizaGold.Api.csproj src/FaizaGold.Api/
RUN dotnet restore src/FaizaGold.Api/FaizaGold.Api.csproj
COPY src/ src/
RUN dotnet publish src/FaizaGold.Api/FaizaGold.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    DataProtection__KeysPath=/keys
RUN mkdir -p /keys && chown app /keys
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "FaizaGold.Api.dll"]
