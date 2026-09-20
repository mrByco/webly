# syntax=docker/dockerfile:1

# Build context is the REPOSITORY ROOT, not this folder: Directory.Build.props and
# Directory.Packages.props live up there and Central Package Management cannot restore without
# them. Build it as `docker build -f Webly.Api/Dockerfile .`.

# ---------------------------------------------------------------------------------------------
# Base runtime
# ---------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

# An HTTP client, purely so the stack's healthcheck can reach /health. The runtime image ships
# neither curl nor wget, and a healthcheck that cannot run does not report "unknown" — it reports
# failure, so the container sits permanently unhealthy and anything gated on that condition never
# starts.
#
# It is copied rather than installed. `apt-get install curl` costs about two seconds of download and
# then several minutes of `apt-get update`, which fetches the whole Ubuntu package index — around
# 25 MB, most of it the universe list — on every cache miss, and on a GitHub runner that step alone
# was the longest thing in the build. This is one static binary and no package manager.
#
# The uclibc tag specifically: busybox:glibc is dynamically linked and would not run here, and the
# distroless-style .NET images have no loader to fix that with.
COPY --from=busybox:uclibc /bin/busybox /usr/bin/busybox
RUN ln -s /usr/bin/busybox /usr/bin/wget

# ---------------------------------------------------------------------------------------------
# Build
# ---------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first, so editing a .cs file does not invalidate the restore layer.
#
# Both Directory.*.props are required. Directory.Packages.props holds every package version
# (the csproj files carry bare PackageReferences and restore fails without it), and
# Directory.Build.props is what sets net10.0 — without it the projects target nothing and
# restore fails with NETSDK1013 rather than anything that names the missing file.
COPY ["Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["Webly.Api/Webly.Api.csproj", "Webly.Api/"]
COPY ["Webly.Services/Webly.Services.csproj", "Webly.Services/"]
COPY ["Webly.Data/Webly.Data.csproj", "Webly.Data/"]

# The API project only, not Webly.slnx: the solution also carries Webly.Tests, which drags in
# Testcontainers and the whole NUnit stack for an image that will never run a test.
RUN dotnet restore "Webly.Api/Webly.Api.csproj"

COPY . .

# UseAppHost=false: the entrypoint is `dotnet Webly.Api.dll`, so the native launcher would be
# an unused platform-specific binary in the image.
RUN dotnet publish "Webly.Api/Webly.Api.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------------------------------------------------------------------------------------------
# Final
# ---------------------------------------------------------------------------------------------
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .

# Plain HTTP on 8080. TLS is terminated by the proxy in front of the stack, and the app reads the
# X-Forwarded-* headers it sets (see Program.cs) so it still knows the request arrived over HTTPS.
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# The key ring directory has to exist here, owned by the account that will write to it. Docker
# seeds a new named volume from the image path it covers, so if this directory were absent the
# volume would be created root-owned and the non-root app below could not write a single key —
# which surfaces as the same "may not be persisted" warning the volume was added to silence, plus
# a permission error, rather than as anything that names the mount.
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys \
    && chown -R $APP_UID:$APP_UID /home/app/.aspnet

# The non-root account the .NET base images define. Its home is where ASP.NET Data Protection puts
# its key ring, which the stack mounts a volume over — nothing in Webly's own auth depends on that
# ring (the session cookies are JWTs signed with Jwt:Key), but the cookie carrying a Google sign-in
# mid-handshake does, so without the volume a redeploy drops anyone standing on Google's consent
# screen back to the login page.
USER $APP_UID

ENTRYPOINT ["dotnet", "Webly.Api.dll"]
