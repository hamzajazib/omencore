namespace OmenCore.Linux.Hardware;

/// <summary>
/// Only one process at a time should write the fans. The daemon's curve engine and a one-off
/// <c>omencore-cli fan --profile ...</c> otherwise overwrite each other every few seconds, which looks like
/// "the setting doesn't stick" or, worse, a fan left at a level nobody chose.
///
/// The lock is an exclusive open (<see cref="FileShare.None"/>), which .NET maps to an advisory flock on Linux, so the
/// kernel drops it when the owner dies and a crash never leaves a stale lock. Who holds it is written to a separate
/// info file, because an exclusive lock also stops other processes reading the lock file itself.
/// </summary>
public sealed class WriterLock : IDisposable
{
    private readonly FileStream _stream;
    private readonly string _infoPath;

    private WriterLock(FileStream stream, string infoPath)
    {
        _stream = stream;
        _infoPath = infoPath;
    }

    /// <summary>/run/omencore when it can be written (root), otherwise the temp folder.</summary>
    public static string DefaultPath()
    {
        const string runDir = "/run/omencore";
        try
        {
            if (OperatingSystem.IsLinux())
            {
                Directory.CreateDirectory(runDir);
                return Path.Combine(runDir, "writer.lock");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not root or /run is read-only: fall through to the temp folder.
        }
        return Path.Combine(Path.GetTempPath(), "omencore-writer.lock");
    }

    /// <summary>
    /// Takes the lock for <paramref name="owner"/> ("daemon", "cli fan"). On failure returns null and
    /// <paramref name="heldBy"/> says who has it, as far as that is known.
    /// </summary>
    public static WriterLock? TryAcquire(string path, string owner, out string heldBy)
    {
        var infoPath = path + ".info";
        heldBy = "";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            try
            {
                File.WriteAllText(infoPath, $"{Environment.ProcessId} {owner} {DateTime.UtcNow:O}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The lock is what matters; the info file only improves the message for the next caller.
            }
            return new WriterLock(stream, infoPath);
        }
        catch (IOException)
        {
            heldBy = ReadHolder(infoPath);
            return null;
        }
    }

    private static string ReadHolder(string infoPath)
    {
        try
        {
            var parts = File.ReadAllText(infoPath).Split(' ', 3);
            return parts.Length == 3 ? $"{parts[1]} (pid {parts[0]}, since {parts[2]})" : "another OmenCore process";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "another OmenCore process";
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
        try { File.Delete(_infoPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover info file is harmless: the lock itself is gone.
        }
    }
}
