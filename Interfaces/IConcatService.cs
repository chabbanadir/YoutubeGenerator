namespace YoutubeGenerator.Interfaces
{
    public interface IConcatService
    {
        Task<string> ConcatAsync(List<string> files, string outputFile);
    }
}
