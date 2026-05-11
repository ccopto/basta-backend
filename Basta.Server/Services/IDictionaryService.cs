using Basta.Server.Entities;

namespace Basta.Server.Services;

/// <summary>
/// Validates whether a submitted word is recognized as valid for the given category type.
/// For CommonWord categories, uses the game-language Hunspell dictionary.
/// For proper-noun categories (City, Country, Name, Animal), uses multilingual offline datasets.
/// </summary>
public interface IDictionaryService
{
    /// <summary>
    /// Returns true if <paramref name="word"/> is a recognized valid word
    /// for the given <paramref name="validationType"/> and <paramref name="language"/>.
    /// </summary>
    /// <param name="word">The raw submitted answer (will be normalized internally).</param>
    /// <param name="language">ISO 639-1 language code, e.g. "en" or "es". Used for Hunspell only.</param>
    /// <param name="validationType">Determines which validator to use.</param>
    bool IsValidWord(string word, string language, CategoryValidationType validationType);
}
