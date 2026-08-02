# --- Stage 1: Base Build (Shared by all) ---
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# 1. Copy CPM and global files for caching
COPY ["Directory.Packages.props", "./"]

# 2. Copy all .csproj files preserving directory structure
COPY ["Services/Shared/Onepunch.Common.Lib/Onepunch.Common.Lib.csproj", "Services/Shared/Onepunch.Common.Lib/"]
COPY ["Services/NotificationApi/NotificationService.csproj", "Services/NotificationApi/"]
COPY ["Services/AuthApi/hrms.auth.api/OnePunch.Auth.Api.csproj", "Services/AuthApi/hrms.auth.api/"]
COPY ["Services/AuthApi/Onepunch.Auth.Core/Onepunch.Auth.Core.csproj", "Services/AuthApi/Onepunch.Auth.Core/"]
COPY ["Services/AuthApi/Onepunch.Auth.Domain/Onepunch.Auth.Domain.csproj", "Services/AuthApi/Onepunch.Auth.Domain/"]
COPY ["Services/AuthApi/Onepunch.Auth.Infrastructure/Onepunch.Auth.Infrastructure.csproj", "Services/AuthApi/Onepunch.Auth.Infrastructure/"]
COPY ["Services/TenantApi/TenantStoreApi/TenantStoreApi.csproj", "Services/TenantApi/TenantStoreApi/"]
COPY ["Services/TenantApi/TenantStoreApi.Core/TenantStoreApi.Core.csproj", "Services/TenantApi/TenantStoreApi.Core/"]
COPY ["Services/TenantApi/TenantStoreApi.Domain/TenantStoreApi.Domain.csproj", "Services/TenantApi/TenantStoreApi.Domain/"]
COPY ["Services/TenantApi/TenantStoreApi.Infrastructure/TenantStoreApi.Infrastructure.csproj", "Services/TenantApi/TenantStoreApi.Infrastructure/"]

# 3. Restore all dependencies once
RUN dotnet restore "Services/AuthApi/hrms.auth.api/OnePunch.Auth.Api.csproj"
RUN dotnet restore "Services/TenantApi/TenantStoreApi/TenantStoreApi.csproj"
RUN dotnet restore "Services/NotificationApi/NotificationService.csproj"

# 4. Copy the rest of the source code
COPY . .

# --- Stage 2: Publish specific targets ---
FROM build AS publish-auth
RUN dotnet publish "Services/AuthApi/hrms.auth.api/OnePunch.Auth.Api.csproj" -c Release -o /app/publish/auth

FROM build AS publish-tenant
RUN dotnet publish "Services/TenantApi/TenantStoreApi/TenantStoreApi.csproj" -c Release -o /app/publish/tenant

FROM build AS publish-notification
RUN dotnet publish "Services/NotificationApi/NotificationService.csproj" -c Release -o /app/publish/notification

# --- Stage 3: Final Runtime Images ---

# Auth Service Image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS auth-service
WORKDIR /app
COPY --from=publish-auth /app/publish/auth .
ENTRYPOINT ["dotnet", "OnePunch.Auth.Api.dll"]

# Tenant Service Image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS tenant-service
WORKDIR /app
COPY --from=publish-tenant /app/publish/tenant .
ENTRYPOINT ["dotnet", "TenantStoreApi.dll"]

# Notification Service Image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS notification-service
WORKDIR /app
COPY --from=publish-notification /app/publish/notification .
ENTRYPOINT ["dotnet", "NotificationService.dll"]
