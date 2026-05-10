using System.Globalization;
using System.Reflection;
using System.Text;
using Basta.Server.Entities;
using WeCantSpell.Hunspell;

namespace Basta.Server.Services;

/// <summary>
/// Singleton service that loads Hunspell dictionaries and proper-noun datasets
/// from embedded resources at application startup. Thread-safe after initialization.
/// </summary>
public sealed class HunspellDictionaryService : IHostedService, IDictionaryService
{
    private readonly ILogger<HunspellDictionaryService> _logger;

    // Hunspell word lists — one per supported game language
    private WordList? _hunspellEn;
    private WordList? _hunspellEs;

    // Multilingual proper-noun HashSets (language-agnostic)
    private HashSet<string> _countries = [];
    private HashSet<string> _cities = [];
    private HashSet<string> _names = [];
    private HashSet<string> _animals = [];

    public HunspellDictionaryService(ILogger<HunspellDictionaryService> logger)
    {
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // IHostedService — warm up on app start
    // -------------------------------------------------------------------------

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Loading dictionary datasets...");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        await Task.WhenAll(
            LoadHunspellAsync("en", cancellationToken),
            LoadHunspellAsync("es", cancellationToken),
            Task.Run(() => _countries = LoadSet("ProperNouns.countries_all.txt"), cancellationToken),
            Task.Run(() => _cities    = LoadSet("ProperNouns.cities_all.txt"),    cancellationToken),
            Task.Run(() => _names     = LoadSet("ProperNouns.names_all.txt"),     cancellationToken),
            Task.Run(() => _animals   = LoadSet("ProperNouns.animals_all.txt"),   cancellationToken)
        );

        _logger.LogInformation(
            "Dictionaries ready — EN:{EnLoaded} ES:{EsLoaded} Countries:{Countries} Cities:{Cities} Names:{Names} Animals:{Animals}",
            _hunspellEn is not null,
            _hunspellEs is not null,
            _countries.Count,
            _cities.Count,
            _names.Count,
            _animals.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // -------------------------------------------------------------------------
    // IDictionaryService
    // -------------------------------------------------------------------------

    public bool IsValidWord(string word, string language, CategoryValidationType validationType)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;

        var normalized = Normalize(word);
        if (normalized.Length < 2)
            return false;

        return validationType switch
        {
            CategoryValidationType.Country => _countries.Contains(normalized),
            CategoryValidationType.City    => _cities.Contains(normalized),
            CategoryValidationType.Name    => _names.Contains(normalized),
            CategoryValidationType.Animal  => _animals.Contains(normalized),
            _                              => CheckHunspell(word.Trim(), language)
        };
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private bool CheckHunspell(string word, string language)
    {
        var dict = language switch
        {
            "es" => _hunspellEs,
            _    => _hunspellEn,
        };
        return dict?.Check(word) ?? false;
    }

    private async Task LoadHunspellAsync(string lang, CancellationToken ct)
    {
        var dicName = lang == "es" ? "es_ES" : "en_US";
        await using var dicStream = GetStream($"Hunspell.{dicName}.dic");
        await using var affStream = GetStream($"Hunspell.{dicName}.aff");

        var wordList = await WordList.CreateFromStreamsAsync(dicStream, affStream, ct);
        if (lang == "es") _hunspellEs = wordList;
        else              _hunspellEn = wordList;
    }

    private static HashSet<string> LoadSet(string resourceSuffix)
    {
        using var stream = GetStream(resourceSuffix);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var set = new HashSet<string>(StringComparer.Ordinal);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var entry = line.Trim();
            if (entry.Length >= 2)
                set.Add(entry); // already normalized by build-datasets.py
        }
        return set;
    }

    private static Stream GetStream(string resourceSuffix)
    {
        var assembly = Assembly.GetExecutingAssembly();
        // Resources are named: Basta.Server.Dictionaries.<resourceSuffix>
        var name = $"Basta.Server.Dictionaries.{resourceSuffix}";
        var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource not found: {name}");
        return stream;
    }

    /// <summary>Lowercase + strip combining diacritics, matching build-datasets.py normalization.</summary>
    internal static string Normalize(string text)
    {
        var nfkd = text.Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(nfkd.Length);
        foreach (var c in nfkd)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString().Trim();
    }
}
