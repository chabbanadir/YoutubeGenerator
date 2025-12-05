using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using YoutubeGenerator.Interfaces;
using YoutubeGenerator.Models;

namespace YoutubeGenerator.Services
{
    public class YoutubeUploader : IYoutubeUploader
    {
        private readonly ILogger<YoutubeUploader> _logger;

        public YoutubeUploader(ILogger<YoutubeUploader> logger)
        {
            _logger = logger;
        }

        public async Task<string> UploadAsync(
            string filePath,
            string title,
            string description,
            YoutubeCredentials creds,
            DateTime publishTimeUtc)
        {
            // Build Google credential manually
            var token = new TokenResponse
            {
                RefreshToken = creds.YoutubeRefreshToken
            };

            var flow = new GoogleAuthorizationCodeFlow(
                new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = new ClientSecrets
                    {
                        ClientId = creds.YoutubeClientId,
                        ClientSecret = creds.YoutubeClientSecret
                    },
                    Scopes = new[] { YouTubeService.Scope.YoutubeUpload }
                });

            var credential = new UserCredential(flow, "user", token);

            var youtube = new YouTubeService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "YoutubeGenerator"
            });



            var video = new Video
            {

                Snippet = new VideoSnippet
                {
                    Title = title,
                    Description = description,
                    CategoryId = "24"
                },
                Status = new VideoStatus
                {
                    PrivacyStatus = "private",   // MUST be private for scheduling
                    PublishAt = publishTimeUtc   // Auto–publish time (UTC)
                }
            };

            using var stream = new FileStream(filePath, FileMode.Open);

            var request = youtube.Videos.Insert(video, "snippet,status", stream, "video/*");

            var response = await request.UploadAsync();

            if (response.Status != UploadStatus.Completed)
                throw new Exception($"Upload failed: {response.Exception?.Message}");

            return request.ResponseBody.Id!;
        }
    }
}
