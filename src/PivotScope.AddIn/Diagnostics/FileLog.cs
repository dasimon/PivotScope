namespace PivotScope.AddIn.Diagnostics;

/// <summary>
/// Log fichier minimal. Un complément qui lève une exception au démarrage est
/// rangé par Excel dans ses « éléments désactivés » : sans trace sur disque, un
/// incident chez un utilisateur est indiagnosticable.
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
            // Le log ne doit jamais faire tomber Excel. Silence assumé.
        }
    }

    /// <summary>Rotation pauvre : on garde les 10 fichiers les plus récents.</summary>
    private static void Prune()
    {
        var files = new DirectoryInfo(Dir).GetFiles("pivotscope-*.log");
        if (files.Length <= 10) return;
        foreach (var f in files.OrderByDescending(f => f.Name).Skip(10))
        {
            try { f.Delete(); } catch { /* fichier verrouillé : on réessaiera demain */ }
        }
    }
}
