using System;
using System.IO;

namespace NvpwrControlBlackwell
{
    /// <summary>
    /// Where the backend keeps its driver-trust cache, VBIOS cache and ROM dumps. This is ProgramData rather than the
    /// install folder, so an in-app update or a new extract folder does not throw away the validation. (bobshmo hit
    /// the same bug in the Prophecy fork: Save MAX / Apply CURRENT went grey after updating into a new folder.)
    /// </summary>
    internal static class StatePaths
    {
        public static readonly string Dir = Path.Combine(AppLog.DirectoryPath, "prophecy-state");

        static StatePaths()
        {
            // One-time carry-over of a cache written beside the exe by an earlier build.
            try
            {
                var old = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "prophecy-state");
                if (!Directory.Exists(old) || Directory.Exists(Dir)) return;
                Directory.CreateDirectory(Dir);
                foreach (var file in Directory.GetFiles(old, "*.txt"))
                    File.Copy(file, Path.Combine(Dir, Path.GetFileName(file)), false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The caches are rebuilt on demand; a failed copy only costs a re-validation.
            }
        }
    }
}
