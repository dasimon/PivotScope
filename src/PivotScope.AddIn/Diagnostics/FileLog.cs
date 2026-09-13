namespace PivotScope.AddIn.Diagnostics;

/// <summary>
/// Minimal file log. An add-in that throws at startup is moved by Excel into
/// its "disabled items": without a trace on disk, an incident on a user's
/// machine cannot be diagnosed.
/// </summary>
public static class FileLog
{
    private static readonly Lock Gate = new();

    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PivotScope", "logs");

    public static void Write(string message, Exception? ex = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                Prune();
                var file = Path.Combine(Dir, $"pivotscope-{DateTime.Now:yyyyMMdd}.log");
                var line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
                if (ex is not null) line += Environment.NewLine + ex;
                File.AppendAllText(file, line + Environment.NewLine);
            }
        }
        catch
        {
            // The log must never bring Excel down. Deliberate silence.
        }
    }

    /// <summary>Poor man's rotation: keep the 10 most recent files.</summary>
    private static void Prune()
    {
        var files = new DirectoryInfo(Dir).GetFiles("pivotscope-*.log");
        if (files.Length <= 10) return;
        foreach (var f in files.OrderByDescending(f => f.Name).Skip(10))
        {
            try { f.Delete(); } catch { /* file locked: we will retry tomorrow */ }
        }
    }
}
