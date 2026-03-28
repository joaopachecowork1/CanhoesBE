FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Canhoes.API/Canhoes.Api.csproj", "Canhoes.API/"]
COPY ["Canhoes.BL/Canhoes.BL.csproj", "Canhoes.BL/"]
COPY ["Canhoes.Infrastructure/Canhoes.Infrastructure.csproj", "Canhoes.Infrastructure/"]

RUN dotnet restore "Canhoes.API/Canhoes.Api.csproj"

COPY . .
WORKDIR /src/Canhoes.API
RUN dotnet publish "Canhoes.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://0.0.0.0:8080

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "Canhoes.Api.dll"]
