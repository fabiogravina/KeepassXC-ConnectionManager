using Kxc.Core.Entries;

namespace Kxc.Core.Search;

/// <summary>
/// Ricerca istantanea su titolo, cartella (il cliente), host, tag e note. Ogni parola digitata deve
/// trovare corrispondenza; il punteggio premia le corrispondenze nel titolo e a inizio parola.
/// </summary>
public static class EntrySearch
{
    public static IReadOnlyList<ConnectionEntry> Search(IEnumerable<ConnectionEntry> entries, string? query)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var tokens = Tokenize(query);
        if (tokens.Count == 0)
        {
            return entries.ToList();
        }

        return entries
            .Select(e => (Entry: e, Score: Score(e, tokens)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Entry.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Entry.EntryPath, StringComparer.Ordinal)
            .Select(x => x.Entry)
            .ToList();
    }

    public static IReadOnlyList<string> Tokenize(string? query) =>
        TextNormalizer.Normalize(query)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Somma dei punteggi delle parole; zero se anche una sola parola non trova nulla.</summary>
    internal static int Score(ConnectionEntry entry, IReadOnlyList<string> tokens)
    {
        var title = TextNormalizer.Normalize(entry.Title);
        var groups = TextNormalizer.Normalize(string.Join(' ', entry.GroupPath));
        var host = TextNormalizer.Normalize(entry.Host);
        var tags = entry.Tags.Select(TextNormalizer.Normalize).ToList();
        var notes = TextNormalizer.Normalize(entry.Notes);

        var total = 0;
        foreach (var token in tokens)
        {
            var best = Math.Max(Math.Max(
                FieldScore(title, token, prefix: 100, wordStart: 80, contains: 60),
                FieldScore(groups, token, prefix: 70, wordStart: 55, contains: 40)),
                FieldScore(host, token, prefix: 50, wordStart: 45, contains: 35));

            if (best == 0 && tags.Any(t => t.StartsWith(token, StringComparison.Ordinal)))
            {
                best = 30;
            }

            if (best == 0 && notes.Contains(token, StringComparison.Ordinal))
            {
                best = 10;
            }

            if (best == 0)
            {
                return 0;
            }

            total += best;
        }

        return total;
    }

    private static int FieldScore(string field, string token, int prefix, int wordStart, int contains)
    {
        if (field.Length == 0)
        {
            return 0;
        }

        if (field.StartsWith(token, StringComparison.Ordinal))
        {
            return prefix;
        }

        var index = field.IndexOf(token, StringComparison.Ordinal);
        if (index < 0)
        {
            return 0;
        }

        while (index >= 0)
        {
            if (!char.IsLetterOrDigit(field[index - 1]))
            {
                return wordStart;
            }

            index = field.IndexOf(token, index + 1, StringComparison.Ordinal);
        }

        return contains;
    }
}
