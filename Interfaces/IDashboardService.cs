using System.Text.Json;

namespace YoutubeGenerator.Interfaces
{
    public interface IDashboardService
    {
        object GetOverview(bool isAuthenticated);
        List<object> GetBags(bool isAuthenticated);
        object GetBagContent(string bagName, bool isAuthenticated);
        void SaveBagContent(string bagName, JsonElement content);
    }
}
