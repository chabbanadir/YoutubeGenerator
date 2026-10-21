namespace YoutubeGenerator.Interfaces
{
    public interface IYoutubeAuthService
    {
        string GetAuthorizationUrl(string kindKey, string redirectUri);
        Task<(string RefreshToken, string AccessToken, string ChannelId, string ChannelTitle)> ExchangeCodeAndFetchChannelAsync(string code, string redirectUri, string? kindKey = null);
        void SaveChannelConnection(string kindKey, string channelId, string refreshToken, string? channelTitle = null);
        void DisconnectChannel(string kindKey);
        (string ClientId, string ClientSecret) GetGlobalCredentials();
        (string ClientId, string ClientSecret) GetCredentialsForKind(string kindKey);
        Task<bool> RefreshAndValidateTokenAsync(string kindKey);
        Task<Dictionary<string, bool>> RefreshAllTokensAsync();
    }
}
