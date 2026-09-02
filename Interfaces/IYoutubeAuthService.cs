namespace YoutubeGenerator.Interfaces
{
    public interface IYoutubeAuthService
    {
        string GetAuthorizationUrl(string kindKey, string redirectUri);
        Task<(string RefreshToken, string AccessToken, string ChannelId, string ChannelTitle)> ExchangeCodeAndFetchChannelAsync(string code, string redirectUri);
        void SaveChannelConnection(string kindKey, string channelId, string refreshToken);
        void DisconnectChannel(string kindKey);
        (string ClientId, string ClientSecret) GetGlobalCredentials();
    }
}
