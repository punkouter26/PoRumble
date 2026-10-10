using PoRumble.Models;

namespace PoRumble.Systems
{
    /// <summary>
    /// Where finished matches are appended. An interface for the reason
    /// <see cref="IRatingStore"/> is one: the path is a platform concern and the tests want
    /// to read what was logged without touching a disk.
    /// </summary>
    public interface IMatchLogStore
    {
        void Append(MatchLogRecord record);
    }
}
