FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish "backend/TuneFinder.Api/TuneFinder.Api.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
CMD ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:$PORT dotnet TuneFinder.Api.dll"]
