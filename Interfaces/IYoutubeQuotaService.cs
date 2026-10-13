using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace YoutubeGenerator.Interfaces
{
    public class ChannelQuotaStatus
    {
        public string ChannelKey { get; set; } = "";
        public int LongVideosLast24h { get; set; }
        public int MaxDailyLongVideos { get; set; }
        public int ShortsLast24h { get; set; }
        public int MaxDailyShorts { get; set; }
        public bool HasPendingLongVideo { get; set; }
        public DateTime? LastLongUploadAt { get; set; }
        public DateTime? LastShortUploadAt { get; set; }
        public DateTime? NextAvailableShortUploadAt { get; set; }
        public string StatusSummary { get; set; } = "";
    }

    public interface IYoutubeQuotaService
    {
        int MaxDailyLongVideos { get; }
        int MaxDailyShorts { get; }
        int MinShortDelayMinutes { get; }
        int MinLongVideoDelayHours { get; }

        bool CanUploadLongVideo(string channelKey, out string reason);
        bool CanUploadShort(string channelKey, out string reason);

        Task RecordUploadAsync(string channelKey, string videoType, string videoId);
        ChannelQuotaStatus GetChannelQuotaStatus(string channelKey);
        List<ChannelQuotaStatus> GetAllChannelsQuotaStatus();
    }
}
