FROM node:24-bookworm-slim AS frontend
WORKDIR /source/frontend/whiteboard
COPY frontend/whiteboard/package*.json ./
RUN npm ci
COPY frontend/whiteboard/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/Lms.Poc/Lms.Poc.csproj src/Lms.Poc/
RUN dotnet restore src/Lms.Poc/Lms.Poc.csproj
COPY src/Lms.Poc/ src/Lms.Poc/
COPY --from=frontend /source/src/Lms.Poc/wwwroot/vendor/whiteboard/ src/Lms.Poc/wwwroot/vendor/whiteboard/
RUN dotnet publish src/Lms.Poc/Lms.Poc.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /out/ ./
RUN mkdir -p /app/App_Data && chown -R app:app /app/App_Data
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Lms.Poc.dll"]
