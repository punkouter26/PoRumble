using System.IO;

namespace PoRumble.Views
{
    /// <summary>
    /// Writes a save file so that a kill mid-write cannot leave it half written.
    ///
    /// <c>File.WriteAllText</c> truncates the live file before it writes, so a phone that is
    /// swiped away during a save keeps an empty league table. Writing beside it and swapping
    /// means the old file stays whole until the new one is complete.
    /// </summary>
    internal static class AtomicFile
    {
        private const string TEMP_SUFFIX = ".tmp";

        public static void WriteAllText(string path, string contents)
        {
            string temp = path + TEMP_SUFFIX;
            File.WriteAllText(temp, contents);

            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }
        }
    }
}
