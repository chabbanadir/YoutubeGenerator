using YoutubeGenerator.Models;

namespace YoutubeGenerator.Interfaces
{
    public interface IYoutubeUploader
    {
        Task<string> UploadAsync(
            string filePath,
            string title,
            string description,
            YoutubeCredentials creds,
            DateTime publishTimeUtc);
    }
}
