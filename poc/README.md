# PoC

本体を作る前に、Outlook on the web とブラウザの振る舞いを確かめるためのコードを置く。ここにあるコードは本体に使わない。

## observer-extension

Outlook on the web を操作したときに、次のことを記録するだけのブラウザ拡張（Manifest V3）である。ページの内容を読むだけで、取得、クリック、書き換えはしない。

| 記録の種類 | 内容 | 確かめたいこと |
| --- | --- | --- |
| `message-opened` | メールを開いたときの URL 中の ID、選択中の会話の `data-convid`、添付らしき要素とその `data-*` 属性 | MessageId と `ConversationId` を画面から取れるか |
| `owa-request` | 添付に関係しそうな Outlook のリクエストの URL（クエリの値は伏せる） | 添付と本文を取得する内部 API の形 |
| `link-clicked` | 本文のリンクのクリック。開いていたメールの ID とリンク先のホスト | ダウンロードとメールの対応付けの手掛かり |
| `tab-created` | 新しいタブと、開いた元のタブ（`openerTabId`） | 同上 |
| `download-created`, `download-complete` | ダウンロードの URL、参照元、保存されたパス | 保存先のパスを拡張機能で取れるか |

### 使い方

1. Chrome または Edge の拡張機能の画面（`chrome://extensions` または `edge://extensions`）でデベロッパーモードを有効にする。
2. 「パッケージ化されていない拡張機能を読み込む」で `poc/observer-extension` を選ぶ。
3. Outlook on the web で次の操作をする。
   1. 添付のあるメールを開く
   2. 添付をダウンロードする
   3. ダウンロードリンクのあるメールを開き、リンクからファイルをダウンロードする
   4. インストールした Outlook のアプリ（PWA）のウィンドウでも、1 を行う
4. ツールバーの拡張機能のアイコンをクリックすると、記録が `ppapd-poc-log.json` として保存される。

記録は、拡張機能を削除するまでブラウザの中に残る。

### 記録を共有するときの注意

URL のクエリの値は記録の時点で伏せている。ただし、次の情報は残る。

- メールの ID
- 添付のファイル名
- リンク先のホスト名
- ダウンロードしたファイルの保存先のパス（Windows のユーザー名を含む）

公開の場所に貼る前に確認すること。

## samples

PRD の受け取り経路と ZIP の扱いを試すためのサンプルを、`make_samples.py` で `samples/out/` に生成する。生成物はコミットしない。

```
python -I poc/samples/make_samples.py
```

暗号化したサンプルには 7-Zip または NanaZip（`7z` コマンド）が要る。パスワードはすべて `Sample-Pass-2026` である。

| ファイル | 暗号化 | ファイル名の文字コード | 試す要件 |
| --- | --- | --- | --- |
| `plain-utf8.zip` | なし | UTF-8（UTF-8 フラグあり） | R-2、F-4.2 |
| `plain-cp932.zip` | なし | CP932（フラグなし） | R-2、F-4.2 |
| `ppap-zipcrypto-utf8.zip` | ZipCrypto | UTF-8 | R-3、F-4.1、F-4.2 |
| `ppap-zipcrypto-cp932.zip` | ZipCrypto | CP932 | R-3、F-4.1、F-4.2 |
| `ppap-aes256-utf8.zip` | AES-256 | UTF-8 | R-3、F-4.1、F-4.2 |
| `ppap-aes256-cp932.zip` | AES-256 | CP932 | R-3、F-4.1、F-4.2 |
| `zip-slip.zip` | なし | ASCII | F-4.3（`../../outside.txt` を含む） |
| `password-mail.txt` | — | — | F-1.3（パスワードメールの本文の例） |

中身はどれも同じで、`図面/` フォルダの下に 2 ファイル、直下に 2 ファイルがある。
