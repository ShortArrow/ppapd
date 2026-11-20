namespace Ppapd.Daemon;

/// <summary>
/// How recent a mail must be to pair with another, and how long a waiting record is kept (docs/PRD.md, F-3.1, F-3.2, F-3.6).
/// These are the defaults; overriding them in <c>rules.yaml</c> is planned in docs/ROADMAP.md.
/// </summary>
internal static class PairingWindow
{
    /// <summary>The largest gap between received times for a pair found by the same <c>ConversationId</c> or the same sender.</summary>
    public static readonly TimeSpan SameConversationOrSender = TimeSpan.FromHours(24);

    /// <summary>The largest gap for a pair found only by the same organization, shorter since other people's mail is easier to mistake.</summary>
    public static readonly TimeSpan SameOrganization = TimeSpan.FromHours(1);

    /// <summary>How long a waiting ZIP record or password candidate is kept after the daemon recorded it.</summary>
    public static readonly TimeSpan KeepWaiting = TimeSpan.FromHours(24);
}