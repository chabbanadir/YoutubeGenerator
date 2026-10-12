using System.Threading.Tasks;

namespace YoutubeGenerator.Interfaces
{
    public interface ITikTokAuthService
    {
        string GetAuthorizationUrl(string kindKey, string redirectUri);
        Task<(string AccessToken, string RefreshToken, string OpenId, string DisplayName, string AvatarUrl, int ExpiresIn, int RefreshExpiresIn)> ExchangeCodeAndFetchUserAsync(string code, string redirectUri);
        void SaveConnection(string kindKey, string openId, string accessToken, string refreshToken, string displayName, string avatarUrl, int expiresIn, int refreshExpiresIn);
        void Disconnect(string kindKey);
        (string ClientKey, string ClientSecret) GetGlobalCredentials();
        Task<string?> GetValidAccessTokenAsync(string kindKey);
        Task<string?> RefreshTokenAsync(string kindKey);
        bool IsConnected(string kindKey);
    }
}
