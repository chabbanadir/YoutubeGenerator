using System.Text.Json;

namespace YoutubeGenerator.Interfaces
{
    public interface IProfileManagementService
    {
        object GetProfiles(bool isAuthenticated);
        object CreateSubKindProfile(JsonElement content);
        object UpdateSubKindProfile(string fileName, JsonElement content);
        void DeleteSubKindProfile(string fileName);
        object CreateKindProfile(JsonElement content);
        object UpdateKindProfile(string fileName, JsonElement content);
        object UpdateKindMinDelay(string fileName, JsonElement body);
        void DeleteKindProfile(string fileName);
        List<string> GetResourceFiles();
    }
}
