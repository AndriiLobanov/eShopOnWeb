#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

# Use the .NET SDK image for building the application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

# Set the working directory
WORKDIR /app

# Copy the solution and project files
COPY *.sln .
COPY . .

# Set the working directory to the PublicApi project
WORKDIR /app/src/PublicApi

# Restore the dependencies
RUN dotnet restore

# Build and publish the project
RUN dotnet publish -c Release -o out

# Use the .NET runtime image for running the application
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# Set the working directory
WORKDIR /app

# Copy the published binaries from the build stage
COPY --from=build /app/src/PublicApi/out ./

# Set environment variables
ENV ASPNETCORE_ENVIRONMENT=Docker
ENV ASPNETCORE_URLS=http://+:8080

# Expose the port for the application
EXPOSE 8080

# Define the entry point for the application
ENTRYPOINT ["dotnet", "PublicApi.dll"]