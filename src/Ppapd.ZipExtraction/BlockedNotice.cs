namespace Ppapd.ZipExtraction;

/// <summary>Why a ZIP was not delivered (docs/PRD.md, F-4.7).</summary>
public enum BlockReason
{
    /// <summary>An entry name points outside the delivery folder (F-4.3).</summary>
    ZipSlip,

    /// <summary>The ZIP exceeds the extraction limits (F-4.6).</summary>
    TooLarge,
}

/// <summary>The text file that tells the user a ZIP was not delivered (docs/PRD.md, F-4.7).</summary>
public static class BlockedNotice
{
    /// <summary>
    /// Writes <c>&lt;zipName&gt;.blocked.txt</c> into <paramref name="destination"/>, holding the text F-4.7 sets for
    /// <paramref name="reason"/>, in UTF-8 with a byte order mark and CRLF line ends. A notice already there under
    /// that name is replaced.
    /// </summary>
    public static void Write(DirectoryInfo destination, string zipName, BlockReason reason) =>
        File.WriteAllText(
            Path.Combine(destination.FullName, zipName + ".blocked.txt"),
            TextFor(reason).Replace("<ZIP の名前>", zipName).ReplaceLineEndings("\r\n"),
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    private static string TextFor(BlockReason reason) => reason switch
    {
        BlockReason.ZipSlip => """
            「<ZIP の名前>」は安全のため展開も配送もしませんでした。
            この ZIP の中に、決められたフォルダーの外にファイルを書き込もうとする名前のファイルが入っていました。普通の ZIP では起きないことです。
            次のどちらかをしてください。
            ・送った人に心当たりがない場合：この ZIP を開かずに、メールごと削除してください。
            ・心当たりがある場合：送った人に連絡し、ZIP を作り直して送ってもらってください。
            このテキストファイルは、確認が済んだら削除してかまいません。

            """,
        BlockReason.TooLarge => """
            「<ZIP の名前>」は配送しませんでした。
            この ZIP は展開すると 2 GB を超えるか、中のファイルが 1 万個を超えるので、保存先の空き容量を守るため途中で展開を止めました。途中まで展開したファイルは消してあります。
            次のどちらかをしてください。
            ・送った人に心当たりがない場合：この ZIP を開かずに、メールごと削除してください。
            ・必要なファイルの場合：送った人に連絡し、いくつかに分けて送り直してもらってください。
            このテキストファイルは、確認が済んだら削除してかまいません。

            """,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };
}
