namespace Ppapd.Daemon;

/// <summary>The state of a mail, sent as the <c>state</c> of docs/PRD.md §7 in camel case, one for each badge of F-2.2 and F-2.4.</summary>
internal enum DeliveryState
{
    /// <summary><c>none</c>: ppapd has not handled the mail.</summary>
    None,

    /// <summary><c>delivered</c>: its files were delivered.</summary>
    Delivered,

    /// <summary><c>waiting</c>: a ZIP or password waits for its pair (F-3.2, F-3.6).</summary>
    Waiting,

    /// <summary><c>extractFailed</c>: no password candidate opened the ZIP (F-3.5).</summary>
    ExtractFailed,

    /// <summary><c>mismatch</c>: the waiting ZIP is gone or changed (F-3.4).</summary>
    Mismatch,

    /// <summary><c>blocked</c>: one of its ZIPs was blocked (F-4.7).</summary>
    Blocked,
}
/// <summary>The one badge of a mail in several states at once (docs/PRD.md, F-2.4).</summary>
internal static class DeliveryStates
{
    /// <summary>
    /// The state F-2.4 puts first among <paramref name="states"/>: blocked, mismatch, extract failed, waiting, delivered,
    /// then none; <see cref="DeliveryState.None"/> when there are none.
    /// </summary>
    public static DeliveryState MostPressing(IEnumerable<DeliveryState> states) => states.Append(DeliveryState.None).MinBy(Urgency);

    private static int Urgency(DeliveryState state) => state switch
    {
        DeliveryState.Blocked => 0,
        DeliveryState.Mismatch => 1,
        DeliveryState.ExtractFailed => 2,
        DeliveryState.Waiting => 3,
        DeliveryState.Delivered => 4,
        _ => 5,
    };
}
