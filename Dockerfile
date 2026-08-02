# Estágio de build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copia apenas o arquivo de projeto e restaura as dependências
COPY ["OllamaWorkerService.csproj", "./"]
RUN dotnet restore "OllamaWorkerService.csproj"

# Copia o resto dos arquivos e compila a aplicação
COPY . .
RUN dotnet build "OllamaWorkerService.csproj" -c Release -o /app/build

# Estágio de publicação
FROM build AS publish
RUN dotnet publish "OllamaWorkerService.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Estágio final (runtime)
# A imagem aspnet do .NET já é baseada em Debian, que possui o /bin/bash nativamente, 
# satisfazendo o requisito do TerminalExecutorService.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=publish /app/publish .

# Define o entrypoint da aplicação
ENTRYPOINT ["dotnet", "OllamaWorkerService.dll"]
