# RetrieveMusicBrainz: the only service with its own Dockerfile. The dump import extracts .tar.bz2
# archives with the external `tar -xj`, which needs bzip2 (not in the standard .NET image).
# Built by deploy.yml from the output of `dotnet publish` (build context: publish/musicbrainz).
FROM mcr.microsoft.com/dotnet/aspnet:10.0

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        bzip2 \
        libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY . .

# non-root user of the .NET images (uid 1654); the /data/mb-dumps volume is a PVC (deploy/k8s/base)
USER $APP_UID
ENTRYPOINT ["dotnet", "CityFy.Retrieve.MusicBrainz.dll"]