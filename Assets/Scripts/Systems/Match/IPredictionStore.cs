using PoRumble.Models;

namespace PoRumble.Systems
{
    /// <summary>
    /// Where the viewer's bank lives between sessions. An interface for the reason
    /// <see cref="IRatingStore"/> is one: the book's rules belong in Systems and the file path
    /// belongs to the platform.
    /// </summary>
    public interface IPredictionStore
    {
        /// <summary>Fills the model from storage. A missing or unreadable file is not an error.</summary>
        void Load(PredictionModel predictions);

        void Save(PredictionModel predictions);
    }
}
