# PoC の観察記録

observer-extension で観察した結果を、観察したときの Outlook と拡張機能のバージョンごとに残す。ここに書いた内容は、そのバージョンでの観察であり、仕様ではない。

## Outlook.com 20261030007.22（個人アカウント）

### 条件

- サービス: Outlook.com（`https://outlook.live.com/mail/0/`）。個人の Microsoft アカウントで、仕事用の Microsoft 365（`outlook.office.com`）ではない。
- Outlook のバージョン: `20261030007.22`。`GetFileAttachment` の URL の `scriptVer` と、Outlook が読み込むスクリプトのパス（`owamail/20261030007.22/`）に出ていた値。
- 拡張機能: observer-extension 0.0.3（表の観察）。追加の観察は Claude in Chrome で行い、拡張機能は使っていない
- 操作: 自分宛てに `ppap-zipcrypto-cp932.zip`（903 バイト）を添付したメールを送り、開いて 5 秒待ち、添付をダウンロードした。

### 観察したこと

| 観察 | 結果 |
| --- | --- |
| URL 中の ID | `/mail/0/inbox/id/<ID>` の `<ID>` は、メール一覧で選択中の項目の `data-convid` と同じ値だった。メール単体の ID ではなく、会話の ID である可能性がある |
| 添付の表示 | メールを開いて 1.5 秒後には見つからず、5 秒後には `role="option"` の要素として見つかった。表示名は「`ppap-zipcrypto-cp932.zip 開く 1 KB`」。その要素にも親要素にも `data-*` 属性はなく、添付 ID は DOM の属性から取れなかった |
| Outlook の内部 API | `https://outlook.live.com/owa/0/service.svc?action=<操作名>&app=Mail` への POST だった。メールを開いたときは `GetConversationItems` と `GetItem` が送られた |
| 添付のダウンロード | ダウンロードの URL は `blob:https://outlook.live.com/<UUID>` の形だった。Outlook がページ内で添付を取得してから blob として保存している。中身を取得したリクエストは、記録の対象（Outlook のホストと `*.office.net`）には見つからなかった |
| 保存先のパス | `chrome.downloads` の完了イベントで、`C:\Users\<ユーザー名>\Downloads\ppap-zipcrypto-cp932.zip` という絶対パスが取れた |
| ダウンロードの中身 | 送った ZIP と SHA-256 が一致した（`ba4f2cf8…`）。Outlook.com は ZIP を書き換えていない。CP932 のファイル名と ZipCrypto はそのまま残り、`7z t` で解凍の検証に通った |

### 追加の観察（Claude in Chrome でページを操作し、通信を記録した）

| 観察 | 結果 |
| --- | --- |
| 添付の中身の取得元 | 添付をクリックすると、`GetItem` の後に `GET https://attachment.outlook.live.net/owa/<メールボックス>/service.svc/s/GetFileAttachment?id=<添付 ID>&isDownload=true&…` が送られた。Outlook のホストとは別のホストである |
| ZIP のクリック | ZIP はプレビューされず、クリックするとそのままダウンロードされた |
| `GetItem` の応答 | `ItemId`（`AAkALgAA…` で始まる、Exchange のメール単体の ID）を含んでいた。URL の会話 ID（`AQQkADAw…`）とは別の値である。この応答に添付 ID は含まれていなかった |
| ページからの通信の横取り | ページの `fetch` と `XMLHttpRequest` を差し替えても、メールを開き直したときの `GetConversationItems` は捕まらなかった。Outlook は開いたことのあるメールをキャッシュから表示し、通信の一部はページの外（ワーカー）から出ている可能性がある |

### 次に確かめること

- `GetFileAttachment` の URL を拡張機能がもう一度取得したとき、同じ中身が返るか。observer-extension 0.0.6 で、取り直した中身のバイト数と SHA-256 を記録する。
- 添付 ID を、添付をクリックする前に得る方法。
- 仕事用の Microsoft 365（`outlook.office.com`）でも同じか。
