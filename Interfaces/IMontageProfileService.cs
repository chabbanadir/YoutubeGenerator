using YoutubeGenerator.Models;

namespace YoutubeGenerator.Interfaces
{
    public interface IMontageProfileService
    {
        MontageProfile GetProfile(string kindKey, string subKindKey);
        MontageProfile EnsureProfileExists(string kindKey, string subKindKey);
        YoutubeCredentials GetYoutubeCredentials(string kindKey);
        void SaveYoutubeCredentials(string kindKey, YoutubeCredentials creds);
    }
}
