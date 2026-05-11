namespace Basta.Server.Entities;

public class Category
{
    public int CategoryId { get; set; }
    public string EnglishName { get; set; } = string.Empty;
    public string SpanishName { get; set; } = string.Empty;

    /// <summary>Determines which validator to use for answers in this category.</summary>
    public CategoryValidationType ValidationType { get; set; } = CategoryValidationType.CommonWord;
}
