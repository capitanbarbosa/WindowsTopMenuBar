namespace WindowsWhisker.Services;

public static class Log
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WindowsWhisker", "log.txt");

    public static void Error(string message, Exception? ex = null)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath, $"{DateTime.Now:u} {message} {ex}\n");
        }
        catch
        {
            // Logging must not take the panel down.
        }
    }
}
