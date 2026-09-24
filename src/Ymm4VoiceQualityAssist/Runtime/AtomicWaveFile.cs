using System.IO;

namespace Ymm4VoiceQualityAssist.Runtime;

public static class AtomicWaveFile
{
    // Staging in the target directory keeps the final move/replace on one volume.
    // Failed copying or a stale source must not truncate the currently usable WAV.
    public static bool TryReplace(string source, string target, Func<bool> canCommit)
    {
        ArgumentNullException.ThrowIfNull(canCommit);
        if (!canCommit()) return false;
        var destination = Path.GetFullPath(target);
        var directory = Path.GetDirectoryName(destination)
            ?? throw new IOException("Voice output directory is unavailable.");
        var staged = Path.Combine(directory, ".vqa-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.Copy(source, staged, overwrite: false);
            if (!canCommit()) return false;
            if (File.Exists(destination)) File.Replace(staged, destination, null);
            else File.Move(staged, destination);
            return true;
        }
        finally
        {
            try { if (File.Exists(staged)) File.Delete(staged); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
