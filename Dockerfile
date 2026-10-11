# ---------------- BUILD -----------------
FROM mcr.microsoft.com/dotnet/nightly/sdk:10.0-preview AS build
WORKDIR /src

COPY *.csproj ./
RUN dotnet restore

COPY . .
RUN dotnet publish YoutubeGenerator.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---------------- RUNTIME -----------------
FROM mcr.microsoft.com/dotnet/nightly/aspnet:10.0-preview AS runtime
WORKDIR /app

# Install ffmpeg, python3, curl, wget, ca-certificates, and the latest release of yt-dlp directly from GitHub
RUN apt-get update && apt-get install -y --no-install-recommends \
      ffmpeg python3 python3-opencv opencv-data ca-certificates wget curl && \
      rm -rf /var/lib/apt/lists/* && \
      curl -L https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp -o /usr/local/bin/yt-dlp && \
      chmod a+rx /usr/local/bin/yt-dlp

# create workspace directories
RUN mkdir -p /app/workspace/bags \
             /app/workspace/ressources \
             /app/workspace/jobs \
             /app/workspace/profiles

COPY --from=build /app/publish /app
COPY Services /app/Services

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "YoutubeGenerator.dll"]
