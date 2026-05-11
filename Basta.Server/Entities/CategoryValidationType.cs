namespace Basta.Server.Entities;

/// <summary>
/// Determines how a category's answers should be validated server-side.
/// </summary>
public enum CategoryValidationType
{
    /// <summary>Validated against the game-language Hunspell dictionary.</summary>
    CommonWord,

    /// <summary>Validated against the multilingual countries dataset.</summary>
    Country,

    /// <summary>Validated against the multilingual cities dataset.</summary>
    City,

    /// <summary>Validated against the multilingual given-names dataset.</summary>
    Name,

    /// <summary>Validated against the multilingual animals dataset.</summary>
    Animal,
}
