# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

# Build image
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy everything from the root of your solution
COPY . .

# Set working directory to the API project folder
WORKDIR /src/hrms-api

# Restore and publish
RUN dotnet restore "hrms-api.csproj"
RUN dotnet publish "hrms-api.csproj" -c Release -o /app/publish

# Final image
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "hrms-api.dll"]