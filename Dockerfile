# Multi-stage build producing a single image that serves both the API and
# the built React app (see RULES_ENGINE_DESIGN.md's deployment notes) -
# Program.cs serves web/dist's output as static files from the same
# ASP.NET Core process that handles /api/*, so there's one container, one
# origin, and no CORS to configure.

# ---- Stage 1: build the React web app ----
FROM node:22-slim AS web-build
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

# ---- Stage 2: build and publish the .NET API (+ engine it references) ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src
COPY DiceFight.slnx ./
COPY src/DiceFight.Engine/ src/DiceFight.Engine/
COPY src/DiceFight.Api/ src/DiceFight.Api/
RUN dotnet publish src/DiceFight.Api/DiceFight.Api.csproj -c Release -o /app/publish

# ---- Stage 3: runtime image ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=api-build /app/publish .
COPY --from=web-build /web/dist ./wwwroot

# Cloud Run injects $PORT and expects the container to listen on it;
# Program.cs reads this at startup. 8080 is just the local/Docker default.
ENV PORT=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "DiceFight.Api.dll"]
