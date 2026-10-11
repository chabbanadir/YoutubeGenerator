namespace YoutubeGenerator.Interfaces
{
    public interface IPathService
    {
        string GetRawDir(string kind, string subKind, string root);
        string GetTrimDir(string kind, string subKind, string root);
        string GetMontageDir(string kind, string subKind, string root);
        string GetShortsDir(string kind, string subKind, string root);
    }
}
