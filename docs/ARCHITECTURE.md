# ppapd アーキテクチャ

ppapd の部品の配置、処理の流れ、データの置き場所を図で示す。要件そのもの（何を満たすか）は [要件定義書](PRD.md) にあり、この文書はそれをどう実現するかを扱う。受け取り経路の番号 R-1〜R-5 は要件定義書の §4 による。

## 部品と境界

```mermaid
flowchart LR
    subgraph Browser["ブラウザ"]
        OWA["Outlook on the web"]
        US["ブラウザ拡張<br/>Manifest V3"]
        LINK["リンク先<br/>OneDrive・転送サービス"]
        OWA -- "DOM・内部 API" --> US
    end

    subgraph PC["利用者の PC（ユーザー権限）"]
        DL["ダウンロードフォルダ"]
        D["デーモン<br/>.NET<br/>Native Messaging のホスト"]
        DB[("SQLite<br/>配送台帳・待機中の ZIP の記録")]
        KEY["鍵ファイル<br/>DPAPI で保護"]
        CFG["rules.yaml"]
        D --- DB
        D --- KEY
        D --- CFG
    end

    DEST["配送先<br/>プロジェクトルートの下"]
    EXP["エクスプローラー"]

    OWA -- "添付のダウンロード" --> DL
    LINK -- "ブラウザのダウンロード" --> DL
    DL -. "完了と保存先のパス<br/>chrome.downloads" .-> US
    US -- "Native Messaging<br/>標準入出力" --> D
    DL -- "ファイルを読む" --> D
    D -- "配送" --> DEST
    D -- "配送先フォルダを開く" --> EXP
```

ブラウザとデーモンの境界は、Native Messaging とダウンロードフォルダの二つである。デーモンはネットワークのポートで待ち受けない。デーモンは Outlook on the web にもインターネットにも接続しない。メールの取得と、リンク先のログインとダウンロードは、すべてブラウザ側が行う。

## 処理の流れ

### 添付（R-1、R-2）

```mermaid
sequenceDiagram
    actor U as 利用者
    participant OWA as Outlook on the web
    participant US as ブラウザ拡張
    participant DL as ダウンロードフォルダ
    participant D as デーモン
    participant DB as SQLite

    U->>OWA: 添付のあるメールを開く
    US->>OWA: 添付をクリックしてダウンロードさせる
    OWA->>DL: ファイルを保存
    US->>D: download<br/>ダウンロードフォルダの下のパス・メールのメタデータ
    D->>D: パスがダウンロードフォルダの下か確認
    D->>DL: ファイルを読む
    D->>D: rules.yaml で配送先を決定<br/>ZIP なら展開・書き出し
    D->>DB: 配送台帳に記録
    D-->>US: 配送済み
    US-->>U: トースト通知
```

ブラウザ拡張は Outlook の認証トークンを扱わず、添付の中身を自分では取得しない。Outlook の画面の添付をクリックしてダウンロードさせ、保存されたものを受け取る。2 つ目以降の添付を保存させるには、Outlook のサイトに Chrome の自動ダウンロードが許可されている必要がある。理由は [ADR 002](adr/002-attachments-via-downloads.md) にある。

### PPAP（R-3）: ZIP メールが先に届いた場合

```mermaid
sequenceDiagram
    actor U as 利用者
    participant OWA as Outlook on the web
    participant US as ブラウザ拡張
    participant DL as ダウンロードフォルダ
    participant D as デーモン
    participant DB as SQLite

    U->>OWA: ZIP メールを開く
    US->>OWA: ZIP をクリックしてダウンロードさせる
    OWA->>DL: 暗号化された ZIP を保存
    US->>D: download<br/>ZIP のパス・メールのメタデータ
    D->>DB: ペアの相手を探す
    DB-->>D: なし
    D->>DB: 待機中の ZIP を記録<br/>パス・SHA-256・MessageId・HMAC
    D-->>US: 待機中

    U->>US: パスワードメールを開く
    US->>D: process<br/>メタデータ・本文・パスワード候補
    D->>DB: ペアの相手を探す
    DB-->>D: 待機中の ZIP の記録
    D->>DL: ZIP を読み、SHA-256 を記録と照合
    D->>D: 解凍・rules.yaml で配送先を決定・書き出し
    D->>DB: 配送台帳に記録
    Note over D: 本文とパスワードをメモリから破棄
    D-->>US: 配送済み
    US-->>U: トースト通知
```

待たせ方の理由は [ADR 001](adr/001-pending-by-reference.md) にある。

### ダウンロードリンク（R-4、R-5）

```mermaid
sequenceDiagram
    actor U as 利用者
    participant US as ブラウザ拡張
    participant L as リンク先
    participant DL as ダウンロードフォルダ
    participant D as デーモン
    participant DB as SQLite

    U->>US: リンクのあるメールを開き、リンクを開く
    U->>L: 必要ならログインし、ダウンロードする
    L->>DL: ファイルを保存
    US->>D: download<br/>ダウンロードフォルダの下のパス・メールのメタデータ
    D->>D: パスがダウンロードフォルダの下か確認
    D->>DL: ファイルを読む
    D->>D: rules.yaml で配送先を決定<br/>ZIP なら展開・書き出し
    D->>DB: 配送台帳に記録
    D-->>US: 配送済み
    US-->>U: トースト通知
```

### 一覧のバッジ

```mermaid
sequenceDiagram
    actor U as 利用者
    participant US as ブラウザ拡張
    participant D as デーモン
    participant DB as SQLite

    U->>US: メール一覧を表示する
    US->>D: status<br/>表示中の MessageId 一覧
    D->>DB: 配送状態を照会
    D-->>US: MessageId ごとの状態
    US-->>U: バッジを表示

    U->>US: バッジをクリック
    US->>D: reveal<br/>MessageId
    D->>DB: 配送先パスを照会
    D->>D: エクスプローラーで開く
```

`reveal` はパスを受け取らない。開くのは配送台帳に記録された配送先だけである。

## データの置き場所

| データ | 置き場所 | 期間 |
| --- | --- | --- |
| 本文、パスワード、平文のアドレスとドメイン | デーモンのメモリ | メッセージの処理中だけ |
| 待機中の ZIP の記録（ダウンロードフォルダの下のパス、SHA-256、MessageId、`ConversationId`、受信日時、アドレスとドメインの HMAC-SHA256） | SQLite | ペアが揃うか、破棄の期限まで |
| 待機中の暗号化された ZIP | ダウンロードフォルダ | ペアが揃うまで |
| 待機中のパスワード候補（DPAPI で暗号化） | SQLite | 解凍を試すか、破棄の期限まで |
| 配送台帳（MessageId、ファイルの SHA-256、配送先パス、ファイル名一覧） | SQLite | 無期限 |
| HMAC の鍵 | 鍵ファイル（DPAPI で保護） | 無期限 |
| 振り分けの規則、送信者の別名 | `rules.yaml` | 利用者が編集する |
| ダウンロードした添付とリンク先のファイル | ダウンロードフォルダ | 配送まで |
| 配送したファイル | 配送先フォルダ | 利用者が管理する |
