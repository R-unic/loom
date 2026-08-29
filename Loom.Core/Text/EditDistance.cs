namespace Loom.Core.Text;

/// <summary>Levenshtein distance, for suggesting a name the user probably meant instead of one that failed to resolve.</summary>
public static class EditDistance
{
    /// <summary>The number of single-character insertions, deletions, or substitutions needed to turn <paramref name="a" /> into <paramref name="b" />.</summary>
    public static int Levenshtein(string a, string b)
    {
        var previousRow = new int[b.Length + 1];
        var currentRow = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previousRow[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            currentRow[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                currentRow[j] = Math.Min(Math.Min(currentRow[j - 1] + 1, previousRow[j] + 1), previousRow[j - 1] + cost);
            }

            (previousRow, currentRow) = (currentRow, previousRow);
        }

        return previousRow[b.Length];
    }
}
