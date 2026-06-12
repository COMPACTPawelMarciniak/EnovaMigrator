namespace EnovaMigrator.Services;

/// <summary>
/// Serwis do fuzzy matching - znajdowanie podobnych ciągów tekstowych.
/// Używa algorytmu Levenshtein Distance oraz Jaro-Winkler similarity.
/// </summary>
public static class FuzzyMatchService
{
    /// <summary>
    /// Oblicza podobieństwo dwóch ciągów (0-100%).
    /// Kombinacja algorytmów dla lepszych wyników.
    /// </summary>
    public static double CalculateSimilarity(string source, string target)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
            return 0;

        // Normalizuj ciągi
        source = NormalizeString(source);
        target = NormalizeString(target);

        if (source == target)
            return 100;

        // Oblicz różne metryki
        var levenshtein = LevenshteinSimilarity(source, target);
        var jaroWinkler = JaroWinklerSimilarity(source, target);
        var containsBonus = ContainsBonus(source, target);

        // Ważona średnia
        return (levenshtein * 0.3 + jaroWinkler * 0.5 + containsBonus * 0.2);
    }

    /// <summary>
    /// Znajduje najlepsze dopasowania dla źródłowego ciągu wśród celów.
    /// </summary>
    public static List<(string Target, double Similarity)> FindBestMatches(
        string source,
        IEnumerable<string> targets,
        int maxResults = 5,
        double minSimilarity = 30)
    {
        return targets
            .Select(t => (Target: t, Similarity: CalculateSimilarity(source, t)))
            .Where(x => x.Similarity >= minSimilarity)
            .OrderByDescending(x => x.Similarity)
            .Take(maxResults)
            .ToList();
    }

    /// <summary>
    /// Normalizuje ciąg do porównania (lowercase, usuń znaki specjalne).
    /// </summary>
    private static string NormalizeString(string s)
    {
        return s.ToLowerInvariant()
            .Replace("-", " ")
            .Replace("_", " ")
            .Replace(".", " ")
            .Replace(",", " ")
            .Trim();
    }

    /// <summary>
    /// Oblicza podobieństwo na podstawie odległości Levenshteina.
    /// </summary>
    private static double LevenshteinSimilarity(string s1, string s2)
    {
        var distance = LevenshteinDistance(s1, s2);
        var maxLength = Math.Max(s1.Length, s2.Length);
        if (maxLength == 0) return 100;

        return (1.0 - (double)distance / maxLength) * 100;
    }

    /// <summary>
    /// Oblicza odległość Levenshteina między dwoma ciągami.
    /// </summary>
    private static int LevenshteinDistance(string s1, string s2)
    {
        var m = s1.Length;
        var n = s2.Length;
        var d = new int[m + 1, n + 1];

        for (var i = 0; i <= m; i++) d[i, 0] = i;
        for (var j = 0; j <= n; j++) d[0, j] = j;

        for (var i = 1; i <= m; i++)
        {
            for (var j = 1; j <= n; j++)
            {
                var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[m, n];
    }

    /// <summary>
    /// Oblicza podobieństwo Jaro-Winkler.
    /// </summary>
    private static double JaroWinklerSimilarity(string s1, string s2)
    {
        var jaro = JaroSimilarity(s1, s2);

        // Jaro-Winkler - dodatkowy bonus za wspólny prefiks
        var prefixLength = 0;
        var maxPrefix = Math.Min(4, Math.Min(s1.Length, s2.Length));

        for (var i = 0; i < maxPrefix; i++)
        {
            if (s1[i] == s2[i])
                prefixLength++;
            else
                break;
        }

        const double p = 0.1; // Scaling factor
        return (jaro + prefixLength * p * (1 - jaro)) * 100;
    }

    /// <summary>
    /// Oblicza podobieństwo Jaro.
    /// </summary>
    private static double JaroSimilarity(string s1, string s2)
    {
        if (s1.Length == 0 && s2.Length == 0)
            return 1.0;

        var matchDistance = Math.Max(s1.Length, s2.Length) / 2 - 1;
        if (matchDistance < 0) matchDistance = 0;

        var s1Matches = new bool[s1.Length];
        var s2Matches = new bool[s2.Length];

        var matches = 0;
        var transpositions = 0;

        // Znajdź dopasowania
        for (var i = 0; i < s1.Length; i++)
        {
            var start = Math.Max(0, i - matchDistance);
            var end = Math.Min(i + matchDistance + 1, s2.Length);

            for (var j = start; j < end; j++)
            {
                if (s2Matches[j] || s1[i] != s2[j]) continue;
                s1Matches[i] = true;
                s2Matches[j] = true;
                matches++;
                break;
            }
        }

        if (matches == 0) return 0;

        // Policz transpozycje
        var k = 0;
        for (var i = 0; i < s1.Length; i++)
        {
            if (!s1Matches[i]) continue;
            while (!s2Matches[k]) k++;
            if (s1[i] != s2[k]) transpositions++;
            k++;
        }

        return ((double)matches / s1.Length +
                (double)matches / s2.Length +
                (matches - transpositions / 2.0) / matches) / 3.0;
    }

    /// <summary>
    /// Bonus jeśli jeden ciąg zawiera drugi.
    /// </summary>
    private static double ContainsBonus(string s1, string s2)
    {
        if (s1.Contains(s2) || s2.Contains(s1))
            return 100;

        // Sprawdź czy słowa się pokrywają
        var words1 = s1.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var words2 = s2.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var commonWords = words1.Intersect(words2, StringComparer.OrdinalIgnoreCase).Count();
        var totalWords = Math.Max(words1.Length, words2.Length);

        if (totalWords == 0) return 0;
        return ((double)commonWords / totalWords) * 100;
    }
}

/// <summary>
/// Reprezentuje sugerowane dopasowanie z oceną podobieństwa.
/// </summary>
public class MatchSuggestion
{
    public int SourceId { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public int TargetId { get; set; }
    public string TargetName { get; set; } = string.Empty;
    public double Similarity { get; set; }
    public string SimilarityDisplay => $"{Similarity:F0}%";

    public override string ToString()
    {
        return $"{SourceName} -> {TargetName} ({SimilarityDisplay})";
    }
}
