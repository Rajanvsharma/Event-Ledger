FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["EventGateway/EventGateway.csproj", "EventGateway/"]
RUN dotnet restore "EventGateway/EventGateway.csproj"
COPY EventGateway/ EventGateway/
WORKDIR "/src/EventGateway"
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "EventGateway.dll"]
