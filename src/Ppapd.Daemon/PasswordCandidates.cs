namespace Ppapd.Daemon;

/// <summary>How many password candidates one try at opening a ZIP uses (docs/PRD.md, F-3.5, F-3.6).</summary>
internal static class PasswordCandidates
{
    /// <summary>
    /// The most candidates tried on one ZIP each time a password mail or a ZIP arrives, the candidates of every paired mail
    /// counted together, and the most kept for one password mail that came first.
    /// </summary>
    public const int MaxTried = 10;

    /// <summary>The first <see cref="MaxTried"/> distinct <paramref name="candidates"/>, in order, and how many were left out.</summary>
    public static (IReadOnlyList<string> Tried, int LeftOut) Limit(IEnumerable<string> candidates)
    {
        var distinct = candidates.Distinct().ToList();
        return (distinct.Take(MaxTried).ToList(), Math.Max(0, distinct.Count - MaxTried));
    }
}
