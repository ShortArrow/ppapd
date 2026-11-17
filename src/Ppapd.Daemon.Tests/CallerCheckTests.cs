namespace Ppapd.Daemon.Tests;

public sealed class CallerCheckTests : IDisposable
{
    private const string Ppapd = "chrome-extension://emhcpcooogencdnpjbgopakclfnojche/";

    private readonly DirectoryInfo hostFolder = Directory.CreateTempSubdirectory("ppapd-test-");

    private FileInfo Manifest => new(Path.Combine(hostFolder.FullName, CallerCheck.ManifestFileName));

    public void Dispose() => hostFolder.Delete(recursive: true);

    [Fact]
    public void The_extension_in_allowed_origins_is_let_in()
    {
        WriteManifest(Ppapd);

        Assert.True(CallerCheck.IsAllowed([Ppapd, "--parent-window=0"], hostFolder));
    }

    [Fact]
    public void Any_of_several_allowed_origins_is_let_in()
    {
        WriteManifest("chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/", Ppapd);

        Assert.True(CallerCheck.IsAllowed([Ppapd], hostFolder));
    }

    [Theory]
    [InlineData("chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/")]
    [InlineData("chrome-extension://EMHCPCOOOGENCDNPJBGOPAKCLFNOJCHE/")]
    [InlineData("chrome-extension://emhcpcooogencdnpjbgopakclfnojche")]
    [InlineData("--parent-window=0")]
    public void A_caller_not_in_allowed_origins_is_refused(string caller)
    {
        WriteManifest(Ppapd);

        Assert.False(CallerCheck.IsAllowed([caller], hostFolder));
    }

    [Fact]
    public void Starting_without_a_caller_is_refused()
    {
        WriteManifest(Ppapd);

        Assert.False(CallerCheck.IsAllowed([], hostFolder));
    }

    [Fact]
    public void A_missing_manifest_refuses_every_caller()
    {
        Assert.False(CallerCheck.IsAllowed([Ppapd], hostFolder));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "name": "jp.shortarrow.ppapd" }""")]
    [InlineData("""{ "allowed_origins": "chrome-extension://emhcpcooogencdnpjbgopakclfnojche/" }""")]
    [InlineData("""{ "allowed_origins": [1, 2] }""")]
    [InlineData("[]")]
    public void A_manifest_that_cannot_be_read_refuses_every_caller(string text)
    {
        File.WriteAllText(Manifest.FullName, text);

        Assert.False(CallerCheck.IsAllowed([Ppapd], hostFolder));
    }

    private void WriteManifest(params string[] origins) =>
        File.WriteAllText(Manifest.FullName, System.Text.Json.JsonSerializer.Serialize(new
        {
            name = "jp.shortarrow.ppapd",
            description = "ppapd",
            path = "Ppapd.Daemon.exe",
            type = "stdio",
            allowed_origins = origins,
        }));
}
