# The production image: the whole website in one container.
#
# Three stages. The first two are throwaway build machines; only the last one
# ships, and it holds the compiled API with the built website inside it - no
# source code, no SDK, no node_modules.
#
#   docker build -f ops/Dockerfile -t webbase-app .
#
# deploy.sh runs this for you. Cloud hosts run it too (see docs/CLOUD.md).

# ---- 1. Build the website (Vue) into plain HTML/JS/CSS ----------------------
FROM node:22-alpine AS client
WORKDIR /build/client
# Dependencies first, on their own, so editing a page doesn't re-download them.
COPY client/package.json client/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY client/ ./
# The guides are built into the site (the /docs pages and the first home page).
COPY docs/ /build/docs/
COPY README.md /build/
RUN npm run build

# ---- 2. Compile the API (C#) ------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY src/WebBase.Api/WebBase.Api.csproj src/WebBase.Api/
RUN dotnet restore src/WebBase.Api/WebBase.Api.csproj
COPY src/ src/
RUN dotnet publish src/WebBase.Api/WebBase.Api.csproj -c Release -o /app --no-restore

# ---- 3. What actually runs --------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /app ./
# The API serves the website from wwwroot (see Program.cs).
COPY --from=client /build/client/dist ./wwwroot
# The CMS's pages travel with the release, so a release is code and content
# together, as committed. Used only when Cms__Enabled=true (docs/CMS.md).
COPY content/ ./content/

# Listen on 8080. A cloud host that sets PORT overrides this (Program.cs).
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Cms__ContentPath=/app/content \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
EXPOSE 8080

# Not root: the image's built-in unprivileged user.
USER $APP_UID

# Which build this is, shown in the dev banner and logged at startup. Last, so
# changing it never invalidates the layers above.
ARG BUILD_ID=""
ENV App__Build=$BUILD_ID

ENTRYPOINT ["dotnet", "WebBase.Api.dll"]
