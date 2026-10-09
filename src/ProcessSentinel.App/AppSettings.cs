using System.IO;
using System.Text.Json;

namespace ProcessSentinel.App;

internal sealed record AppSettings
{
    public bool IncludeProgramTree { get; init; } = true;
    public bool RiskOnly { get; init; }

    internal static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProcessSentinel", "settings.json");

    internal static AppSettings Load(out string? error)
    {
        error = null;
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new()
                : new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = "无法读取本地设置，已使用默认设置：" + ex.Message;
            return new();
        }
    }

    internal void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
