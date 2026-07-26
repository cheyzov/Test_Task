FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Test_Task/Test_Task.csproj Test_Task/
RUN dotnet restore Test_Task/Test_Task.csproj

COPY . .
RUN dotnet publish Test_Task/Test_Task.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Test_Task.dll"]
