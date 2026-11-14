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

### 連続したダウンロード（Outlook.com 20261030007.23、Google Chrome 156.0.8078.81、Claude in Chrome でページのスクリプトとして実行）

自分宛てに `sample.pdf`、`plain-utf8.zip`、`ppap-aes256-utf8.zip` の 3 つを添付したメールを送り、開いた後に 2 つの ZIP を 1.5 秒の間隔でスクリプトからクリックした。

| 観察 | 結果 |
| --- | --- |
| 1 つ目の ZIP | ダウンロードフォルダに保存された。送った ZIP と SHA-256 が一致した |
| 2 つ目の ZIP | 保存されなかった。Outlook は添付を取得し（`GetFileAttachment` が HTTP 200）、1085 バイトの blob を作って `download` 属性付きのリンクを `click()` しており、添付の表示は「ダウンロード済み」に変わった |
| Chrome の確認 | 2 つ目で、Chrome が複数ファイルのダウンロードを許可するかブロックするかを尋ねる確認を出した。確認に答えるまで、マウスで実際にクリックしても（`navigator.userActivation.isActive` が true）保存されなかった |
| 確認が見えるとき | 確認はタブの外（ブラウザの画面）に出る。ページのスクリプトからも、ダウンロードフォルダからも、出ていることはわからない |

Chrome は、一つのサイトが 2 つ目以降のファイルをダウンロードしようとすると、利用者に許可を求める。利用者が一度「許可」を選ぶまで、拡張機能が添付を続けて自動でダウンロードさせることはできない。

利用者が確認で「許可」を選んだ後の観察は次のとおりである。

| 観察 | 結果 |
| --- | --- |
| 確認を待っていたダウンロード | 「許可」を選んだ時点で、確認を待つ間にクリックした 7 回分がすべて保存された（`ppap-aes256-utf8.zip`、`ppap-aes256-utf8 (1).zip` など）。どれも送った ZIP と SHA-256 が一致した |
| PDF の添付 | 添付の要素の中の「その他の操作」ボタンを `click()` し、開いたメニューのうち `role="menuitem"` で「ダウンロード」を含む要素を `click()` すると、プレビューを開かずに保存された。送った PDF と SHA-256 が一致した |
| 許可した後の連続したダウンロード | PDF の 8 秒後に ZIP をスクリプトからクリックすると、確認なしに保存された |
| 添付の表示上の大きさ | Outlook は `sample.pdf`（328 バイト）を「524 バイト」、`plain-utf8.zip`（625 バイト）を「867 バイト」と表示した。保存されたファイルは送ったものと同じ大きさだった |

### Native Messaging（`poc/native-messaging` の拡張機能 0.0.1、Google Chrome 156.0.8078.81、Windows 11 Pro 10.0.26300）

メールを開いたときと一覧の表示が変わったときに、拡張機能が一覧の項目の `data-convid` を一本の `connectNative` の接続でホストへ送り、ホストが返した ID の項目に「済」の印を付けた。

| 観察 | 結果 |
| --- | --- |
| ホストの登録 | ホストのマニフェストを `HKCU\Software\Google\Chrome\NativeMessagingHosts\<名前>` に登録するだけで、管理者の権限なしに Chrome から呼べた |
| ホストの起動 | 最初の送信で Chrome がホストを起動した。引数は `chrome-extension://<拡張機能の ID>/ --parent-window=0` だった |
| 一覧への印 | 受信トレイの一覧の項目に「済」が付いた |
| 往復の時間 | ホストの起動を含む最初の往復は 273 ミリ秒、起動後の往復は 2 ミリ秒だった（コンテンツスクリプトの `performance.now()` で測った） |
| 操作のない間 | 4 分間操作しなくても、ホストは同じプロセスのまま動き続けた。その後のフォルダの切り替えも同じプロセスが受け取った。`connectNative` の接続が開いている間、拡張機能のバックグラウンドは止まらなかった |
| 一覧の項目の ID | Outlook.com の一覧の項目は、どれも `data-convid` を持っていた。受信トレイで 14〜18 件、送信済みで 13〜16 件が、一度に DOM にあった |

### 本文のリンクからのダウンロード（`poc/native-messaging` の拡張機能 0.0.2、Google Chrome 156.0.8078.81）

別の Microsoft 365 のアドレスから、OneDrive の共有リンク（`1drv.ms`）を本文に入れたメールを Outlook.com 宛てに送った。メールを開いてリンクをクリックし、開いた OneDrive のページで「Download」を押した。

| 観察 | 結果 |
| --- | --- |
| 本文のリンク | Outlook は、リンクを Safe Links（`jpn01.safelinks.protection.outlook.com`）の URL で包んでいた。`target="_blank"`、`rel="noopener noreferrer"` が付いていた |
| リンクのクリック | コンテンツスクリプトがクリックを捕まえ、そのタブの ID と、開いていたメールが分かる状態で記録できた。リンク先のホストは Safe Links のホストだった |
| 新しいタブ | クリックの 0.2 秒後に新しいタブが作られた。`openerTabId` は Outlook のタブの ID ではなく、別のタブの ID だった |
| リンク先 | 新しいタブは約 3 秒後に `onedrive.live.com` へ移り、ログインなしでファイルの画面が開いた |
| ダウンロード | 「Download」で、`onedrive.live.com` から送った ZIP と SHA-256 が一致するファイルが保存された。`chrome.downloads` のダウンロードの URL、最終 URL、参照元は、どれも `onedrive.live.com` だった。ダウンロードにはタブの ID が付かない |
| 操作のない間の接続 | 拡張機能を読み込み直した直後に 1 通だけ送り、その後 5 分間何も送らなかったホストは、ちょうど 5 分後に標準入力が閉じられて終わった。次の送信で、Chrome は新しいホストを起動した |

同じ手順を Google Drive の共有リンク（`drive.google.com/open?id=…`）でも行った。ファイルの持ち主の Google アカウントでログインしたブラウザで開いた。

| 観察 | 結果 |
| --- | --- |
| 本文のリンク | OneDrive と同じく Safe Links で包まれ、`target="_blank"`、`rel="noopener noreferrer"` が付いていた |
| 新しいタブ | クリックの 0.14 秒後に作られ、`openerTabId` は今度は Outlook のタブの ID だった。OneDrive のときと結果が違い、手がかりとしては当てにできない |
| リンク先 | 新しいタブは約 2 秒後に `drive.google.com` へ移った |
| ダウンロード | 「ダウンロード」を押すと、Google Drive は一時的なタブ（`openerTabId` は Google Drive のタブ）を作ってからダウンロードした。ダウンロードの URL と最終 URL は `drive.usercontent.google.com`、参照元は `drive.google.com` だった。保存された ZIP は送った ZIP と SHA-256 が一致した |

OneDrive のフォルダの共有リンク（`1drv.ms/f/…`）でも同じ手順を行った。フォルダには `plain-utf8.zip` と `sample.pdf` を入れた。フォルダの持ち主の Microsoft アカウントでログインしたブラウザで開いたので、OneDrive は持ち主の画面（「マイファイル」）でフォルダを表示した。

| 観察 | 結果 |
| --- | --- |
| 新しいタブ | クリックの 0.19 秒後に作られ、約 3 秒後に `onedrive.live.com` へ移った。`openerTabId` は Outlook のタブではなかった |
| ダウンロード | フォルダの画面の「ダウンロード」で、`test.zip`（フォルダ名の ZIP）が 1 つ保存された。ダウンロードの URL と最終 URL は `australiasoutheast1-mediap.svc.ms` で、参照元は空だった |
| ZIP の中身 | エントリは `test/plain-utf8.zip` と `test/sample.pdf` で、フォルダ名の下に入っていた。ファイル名は UTF-8 フラグ付き、圧縮なし（無圧縮で格納）で、どちらも送ったファイルと SHA-256 が一致した。ZIP の中に ZIP が入っている |

ファイルの共有リンクでは、ダウンロードの参照元のホストは、リンクを開いたタブが移ったホストと一致した。フォルダの共有リンクでは参照元が空で、参照元のホストでは照合できなかった。ダウンロードの URL のホストは、Google Drive では一致しなかった。

ダウンロードの記録だけでは、どのメールのリンクから始まったかはわからない。手がかりになるのは、リンクのクリックの時刻とタブ、その直後に作られたタブ、そのタブが移ったホストと、ダウンロードの参照元のホストの一致である。

### ダウンロードが始まったときの前面のタブ（`poc/native-messaging` の拡張機能 0.0.3、Google Chrome 156.0.8078.81、Claude in Chrome でリンクとダウンロードのボタンをクリック）

`chrome.downloads.onCreated` で、最後にフォーカスされたウィンドウの前面のタブ（`chrome.tabs.query({ active: true, lastFocusedWindow: true })`）を記録した。OneDrive のフォルダの共有リンクとファイルの共有リンクで、メールのリンクのクリックからダウンロードまでを 1 回ずつ行った。

| 観察 | 結果 |
| --- | --- |
| 新しいタブ | どちらも、リンクのクリックの 0.18〜0.19 秒後に作られた。`openerTabId` は、どちらも Outlook のタブではなかった |
| フォルダの共有リンク | ダウンロードの参照元は前回と同じく空だった。前面のタブは、リンクのクリックの直後に作られたタブと同じ ID で、ホストは `onedrive.live.com` だった |
| ファイルの共有リンク | 参照元は `onedrive.live.com` だった。前面のタブは、リンクのクリックの直後に作られたタブと同じ ID だった |
| 間に入った別のタブ | ファイルの共有リンクでは、新しいタブが作られてから移るまでの間に、別のウィンドウの利用者のタブが別のホストへ移った記録が入った。新しいタブの作成はクリックの直後で、取り違えはなかった |

参照元が空のフォルダの共有リンクでも、ダウンロードが始まったときの前面のタブで、リンクを開いたタブまでたどれた。この試しでは、ダウンロードのボタンを押したタブが前面にあった。ボタンを押した後にダウンロードが始まるまでの間に、利用者が別のタブへ移った場合は試していない。

### 次に確かめること

- Chrome やタブを閉じたとき、ホストのプロセスが終わるか。
- 添付 ID を、添付をクリックする前に得る方法。
- 仕事用の Microsoft 365（`outlook.office.com`）でも同じか。
