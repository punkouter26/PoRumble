using PoRumble.Models;

namespace PoRumble.Systems
{
    /// <summary>
    /// Where the standings live between sessions.
    ///
    /// An interface because the rating maths belongs in Systems and the file path belongs to
    /// the platform: <see cref="RatingSystem"/> should not know whether the table is on disk,
    /// in PlayerPrefs or nowhere at all. The training scenes bind nothing, which is what stops
    /// a run writing a league table nobody asked for.
    /// </summary>
    public interface IRatingStore
    {
        /// <summary>Fills the model from storage. A missing or unreadable table is not an error.</summary>
        void Load(RatingModel ratings);

        void Save(RatingModel ratings);
    }
}
