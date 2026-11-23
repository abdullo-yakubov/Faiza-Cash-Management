FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/ ./src/
RUN dotnet publish src/FaizaCash.Api/FaizaCash.Api.csproj -c Release -o /out
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl \
    && mkdir -p /usr/share/postgresql-common/pgdg \
    && curl --fail --silent --show-error https://www.postgresql.org/media/keys/ACCC4CF8.asc -o /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc \
    && echo 'deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] https://apt.postgresql.org/pub/repos/apt noble-pgdg main' > /etc/apt/sources.list.d/pgdg.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends postgresql-client-18 \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /var/lib/faiza/backups \
    && chown "$APP_UID" /var/lib/faiza/backups
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080
ENV Backups__PostgresBin=/usr/lib/postgresql/18/bin
ENV Backups__Directory=/var/lib/faiza/backups
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet","FaizaCash.Api.dll"]
