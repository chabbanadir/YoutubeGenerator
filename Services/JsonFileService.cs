using System.Text.Encodings.Web;
using System.Text.Json;

namespace YoutubeGenerator.Services
{
    public static class JsonFileService
    {
        public static T Load<T>(string file)
        {
            if (!File.Exists(file))
                return Activator.CreateInstance<T>();

            var json = File.ReadAllText(file);

            return string.IsNullOrWhiteSpace(json)
                ? Activator.CreateInstance<T>()
                : JsonSerializer.Deserialize<T>(json)!;
        }

        public static void Save<T>(string file, T obj)
        {
            var dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            File.WriteAllText(file, json);
        }
    }
}
