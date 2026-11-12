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

### 拡張機能からの取り直し（observer-extension 0.0.6、Google Chrome 156.0.8078.81、Windows platformVersion 19.0.0）

| 観察 | 結果 |
| --- | --- |
| Outlook 自身の取得 | 添付をクリックすると、ページが `XMLHttpRequest` で `GetFileAttachment` を GET した。URL の `X-OWA-CANARY` の値は `X-OWA-CANARY_cookie_is_null_or_empty` だった |
| 拡張機能からの取り直し | 同じ URL を拡張機能のバックグラウンドから `credentials: "include"` で GET すると、HTTP 400 で中身は空だった。URL だけでは添付を取り直せない。Outlook は URL 以外（リクエストヘッダーなど）で認証していると考えられる |
| ダウンロード | Outlook が取得した中身は blob として保存され、`chrome.downloads` で `C:\Users\<ユーザー名>\Downloads\ppap-zipcrypto-cp932 (3).zip`（903 バイト）という絶対パスが取れた |

### 添付取得の認証（observer-extension 0.0.7、Google Chrome 156.0.8078.81）

`GetFileAttachment` に付いていたリクエストヘッダーの名前を記録した（値は記録していない）。

| 送り手 | ヘッダー名 | 結果 |
| --- | --- | --- |
| Outlook のページ | `authorization`、`x-anchormailbox`、`origin`、`referer` などで、`cookie` はない | 成功（903 バイトをダウンロード） |
| 拡張機能のバックグラウンド | `cookie` などで、`authorization` はない | HTTP 400、中身は空 |

Outlook.com は添付の取得を、Cookie ではなく `Authorization` ヘッダーのトークンで認証している。拡張機能から添付を取り直すには、このトークンを手に入れて付ける必要がある。

### スクリプトからの添付のダウンロード（Google Chrome 156.0.8078.81、Claude in Chrome でページのスクリプトとして実行）

| 観察 | 結果 |
| --- | --- |
| ZIP の添付 | メールを開いた後、`aria-label` が `.zip` を含む `role="option"` の要素を探して `click()` を呼ぶと、利用者の操作なしにダウンロードが始まり、ダウンロードフォルダに 903 バイトの ZIP が保存された |
| ZIP 以外の添付 | 確かめていない。PDF などはクリックするとプレビューが開くので、ダウンロードには別の操作（添付のメニューの「ダウンロード」）が要る見込みである |
| 連続した自動ダウンロード | 確かめていない。Chrome は、一つのサイトが利用者の操作なしに複数のファイルをダウンロードしようとすると、許可を求めることがある |

### 次に確かめること

- ZIP 以外の添付（PDF など）を、スクリプトからダウンロードさせる操作。
- 複数の添付を続けてダウンロードさせたときに、Chrome が許可を求めるか。

- 添付 ID を、添付をクリックする前に得る方法。
- 仕事用の Microsoft 365（`outlook.office.com`）でも同じか。
