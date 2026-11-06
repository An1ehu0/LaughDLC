using System.Text;

namespace LaughDlc.Services;

internal static class AppLog
{
    private static readonly object Gate = new();

    public static string AppDataDirectory
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
            {
                appData = AppContext.BaseDirectory;
            }

            return Path.Combine(appData, "LaughDlc");
        }
    }

    public static string LogFilePath => Path.Combine(AppDataDirectory, "logs", "app.log");

    public static void Info(string message)
    {
        Write("INFO", message, null);
    }

    public static void Error(string message, Exception? exception = null)
    {
        Write("ERROR", message, exception);
    }

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            var builder = new StringBuilder();
            builder.Append(DateTimeOffset.Now.ToString("O"));
            builder.Append(" [").Append(level).Append("] ");
            builder.Append(message);

            if (exception is not null)
            {
                builder.AppendLine();
                builder.Append(exception);
            }

            builder.AppendLine();

            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
                File.AppendAllText(LogFilePath, builder.ToString(), new UTF8Encoding(false));
            }
        }
        catch
        {
            // Logging must never prevent the application from running.
        }
    }
}
