# Use the official .NET SDK image for building the application
# Use the official .NET 9 SDK image as the build environment
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy the project file and restore dependencies
COPY ["OfficeTaskManagement.csproj", "./"]
RUN dotnet restore "OfficeTaskManagement.csproj"

# Copy the remaining source code
COPY . .

# Build the application
RUN dotnet build "OfficeTaskManagement.csproj" -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish "OfficeTaskManagement.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Use the official .NET 9 ASP.NET Core runtime image for the final image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Expose port 8080
EXPOSE 8080

# Configure the entry point for the application
ENTRYPOINT ["dotnet", "OfficeTaskManagement.dll"]
