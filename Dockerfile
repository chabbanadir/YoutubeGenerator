# ---------------- BUILD -----------------
FROM mcr.microsoft.com/dotnet/nightly/sdk:10.0-preview AS build
WORKDIR /src

COPY *.csproj ./
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# ---------------- RUNTIME -----------------
FROM mcr.microsoft.com/dotnet/nightly/aspnet:10.0-preview AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends \
      ffmpeg yt-dlp ca-certificates && \
      rm -rf /var/lib/apt/lists/*

# create workspace directories
RUN mkdir -p /app/workspace/bags \
             /app/workspace/ressources \
             /app/workspace/jobs \
             /app/workspace/profiles

COPY --from=build /app/publish /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "YoutubeGenerator.dll"]
