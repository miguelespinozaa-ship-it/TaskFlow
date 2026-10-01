# syntax=docker/dockerfile:1
# Contexto de build: la raíz del repositorio (docker build -f src/TaskFlow.Api/Dockerfile .)

# ---------- build: SDK completo, no llega a la imagen final ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Primero solo los .csproj: mientras no cambien las dependencias, el `restore` queda en caché
# y un cambio de código no vuelve a descargar los paquetes.
COPY global.json Directory.Build.props ./
COPY src/TaskFlow.Domain/TaskFlow.Domain.csproj src/TaskFlow.Domain/
COPY src/TaskFlow.Application/TaskFlow.Application.csproj src/TaskFlow.Application/
COPY src/TaskFlow.Infrastructure/TaskFlow.Infrastructure.csproj src/TaskFlow.Infrastructure/
COPY src/TaskFlow.Api/TaskFlow.Api.csproj src/TaskFlow.Api/
RUN dotnet restore src/TaskFlow.Api/TaskFlow.Api.csproj

COPY src/ src/
RUN dotnet publish src/TaskFlow.Api/TaskFlow.Api.csproj -c Release -o /app --no-restore

# ---------- runtime: solo ASP.NET, sin SDK ni código fuente ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .

# Usuario sin privilegios que ya trae la imagen base: si alguien compromete el proceso, no es root.
USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "TaskFlow.Api.dll"]
