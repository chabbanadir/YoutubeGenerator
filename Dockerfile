# ---------------- BUILD -----------------
FROM mcr.microsoft.com/dotnet/nightly/sdk:10.0-preview AS build
WORKDIR /src

# Copy csproj and restore as distinct layers
COPY *.csproj ./
RUN dotnet restore

# Copy everything else and build
COPY . .
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# ---------------- RUNTIME -----------------
FROM mcr.microsoft.com/dotnet/nightly/aspnet:10.0-preview AS runtime
WORKDIR /app

# Install ffmpeg + yt-dlp (APT version)
RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      ffmpeg \
      yt-dlp \
      ca-certificates \
 && rm -rf /var/lib/apt/lists/*

# create workspace directories expeccted by the app 
RUN mkdir -p /app/workspace/bags \
             /app/workspace/ressources \
             /app/workspace/jobs \
             /app/workspace/profiles 

# copy published app
COPY --from=build /app/publish /app

#expose default port 
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

RUN useradd -m chabba && chown -R chabba:chabba /app
USER chabba

HEALTHCHECK --interval=15s --timeout=3s --retries=3 \
  CMD wget -qO- --timeout=2 http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "YoutubeGenerator.dll"]