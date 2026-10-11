using FluentResults;
using System.Runtime.InteropServices;
using YoutubeGenerator.Interfaces;

namespace YoutubeGenerator.Services
{
    public class PathService : IPathService
    {
        public  string GetRawDir(string kind, string subKind, string root)
        {
            var result = Path.Combine(root, "jobs", kind ?? "default", "raw", subKind ?? "default");
            Directory.CreateDirectory(result);
            return result;
        }

        public string GetMontageDir(string kind, string subKind, string root)
        {
            var result = Path.Combine(root, "jobs", kind ?? "default", "montage", subKind ?? "default");
            Directory.CreateDirectory(result);
            return result;
        }

        public string GetTrimDir(string kind, string subKind, string root)
        {
            var result = Path.Combine(root, "jobs", kind ?? "default", "trim", subKind ?? "default");
            Directory.CreateDirectory(result);
            return result;
        }

        public string GetShortsDir(string kind, string subKind, string root)
        {
            var result = Path.Combine(root, "jobs", kind ?? "default", "shorts", subKind ?? "default");
            Directory.CreateDirectory(result);
            return result;
        }
    }
}
