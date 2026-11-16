using System.Text;

namespace Ppapd.ZipExtraction.Tests;

public sealed class BlockedNoticeTests : IDisposable
{
    private readonly DirectoryInfo destination = Directory.CreateTempSubdirectory("ppapd-test-");

    public void Dispose() => destination.Delete(recursive: true);

    [Fact]
    public void A_zip_blocked_for_zip_slip_gets_the_zip_slip_notice_beside_where_it_would_have_gone()
    {
        BlockedNotice.Write(destination, "見積書.zip", BlockReason.ZipSlip);

        Assert.Equal(
            """
            「見積書.zip」は安全のため展開も配送もしませんでした。
            この ZIP の中に、決められたフォルダーの外にファイルを書き込もうとする名前のファイルが入っていました。普通の ZIP では起きないことです。
            次のどちらかをしてください。
            ・送った人に心当たりがない場合：この ZIP を開かずに、メールごと削除してください。
            ・心当たりがある場合：送った人に連絡し、ZIP を作り直して送ってもらってください。
            このテキストファイルは、確認が済んだら削除してかまいません。

            """.ReplaceLineEndings("\r\n"),
            File.ReadAllText(Path.Combine(destination.FullName, "見積書.zip.blocked.txt")));
    }

    [Fact]
    public void A_zip_blocked_for_its_size_gets_the_size_notice()
    {
        BlockedNotice.Write(destination, "図面一式.zip", BlockReason.TooLarge);

        Assert.Equal(
            """
            「図面一式.zip」は配送しませんでした。
            この ZIP は展開すると 2 GB を超えるか、中のファイルが 1 万個を超えるので、保存先の空き容量を守るため途中で展開を止めました。途中まで展開したファイルは消してあります。
            次のどちらかをしてください。
            ・送った人に心当たりがない場合：この ZIP を開かずに、メールごと削除してください。
            ・必要なファイルの場合：送った人に連絡し、いくつかに分けて送り直してもらってください。
            このテキストファイルは、確認が済んだら削除してかまいません。

            """.ReplaceLineEndings("\r\n"),
            File.ReadAllText(Path.Combine(destination.FullName, "図面一式.zip.blocked.txt")));
    }

    [Fact]
    public void The_notice_is_utf8_with_a_byte_order_mark_so_any_windows_editor_reads_its_japanese()
    {
        BlockedNotice.Write(destination, "a.zip", BlockReason.ZipSlip);

        var bytes = File.ReadAllBytes(Path.Combine(destination.FullName, "a.zip.blocked.txt"));
        Assert.Equal(Encoding.UTF8.Preamble.ToArray(), bytes[..3]);
    }

    [Fact]
    public void A_second_block_of_the_same_zip_name_replaces_the_earlier_notice()
    {
        BlockedNotice.Write(destination, "a.zip", BlockReason.ZipSlip);

        BlockedNotice.Write(destination, "a.zip", BlockReason.TooLarge);

        Assert.StartsWith("「a.zip」は配送しませんでした。", File.ReadAllText(Path.Combine(destination.FullName, "a.zip.blocked.txt")));
    }
}
