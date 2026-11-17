namespace Ppapd.Daemon.Tests;

public sealed class PairingWindowTests
{
    [Fact]
    public void The_defaults_are_those_the_requirements_set()
    {
        Assert.Equal(TimeSpan.FromHours(24), PairingWindow.SameConversationOrSender);
        Assert.Equal(TimeSpan.FromHours(1), PairingWindow.SameOrganization);
        Assert.Equal(TimeSpan.FromHours(24), PairingWindow.KeepWaiting);
    }
}