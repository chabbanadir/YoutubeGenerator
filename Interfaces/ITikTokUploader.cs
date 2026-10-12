using System.Threading.Tasks;

namespace YoutubeGenerator.Interfaces
{
    public interface ITikTokUploader
    {
        Task<(bool Success, string PublishId, string Message)> UploadVideoAsync(
            string kindKey,
            string videoPath,
            string title,
            string privacyLevel = "SELF_ONLY");

        Task<(string Status, string FailReason)> CheckPublishStatusAsync(
            string kindKey,
            string publishId);
    }
}
