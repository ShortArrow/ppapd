# ppapd アーキテクチャ

ppapd の部品の配置、処理の流れ、データの置き場所を図で示す。要件そのもの（何を満たすか）は [要件定義書](PRD.md) にあり、この文書はそれをどう実現するかを扱う。受け取り経路の番号 R-1〜R-5 は要件定義書の §4 による。

## 部品と境界

```mermaid
flowchart LR
    subgraph Browser["ブラウザ"]
        OWA["Outlook on the web"]
        US["UserScript"]
        LINK["リンク先<br/>OneDrive・転送サービス"]
        OWA -- "DOM・内部 API" --> US
    end

    subgraph PC["利用者の PC（ユーザー権限）"]
        DL["ダウンロードフォルダ"]
        D["デーモン<br/>ASP.NET Core Minimal API<br/>タスクトレイ常駐"]
        DB[("SQLite<br/>配送台帳・待機中の参照")]
        KEY["鍵ファイル<br/>DPAPI で保護"]
        CFG["rules.yaml"]
        D --- DB
        D --- KEY
        D --- CFG
    end

    DEST["配送先<br/>プロジェクトルートの下"]
    EXP["エクスプローラー"]

    LINK -- "ブラウザのダウンロード" --> DL
    US -- "HTTP 127.0.0.1:18080<br/>Bearer トークン" --> D
    DL -- "ファイルを読む" --> D
    D -- "配送" --> DEST
    D -- "配送先フォルダを開く" --> EXP
```

ブラウザとデーモンの境界は、ループバックの HTTP とダウンロードフォルダの二つである。デーモンは Outlook on the web にもインターネットにも接続しない。メールの取得と、リンク先のログインとダウンロードは、すべてブラウザ側が行う。

## 処理の流れ

### 添付（R-1、R-2）

```mermaid
sequenceDiagram
    actor U as 利用者
    participant OWA as Outlook on the web
    participant US as UserScript
    participant DL as ダウンロードフォルダ
    participant D as デーモン
    participant DB as SQLite

    U->>OWA: 添付のあるメールを開き、添付をダウンロードする
    OWA->>DL: ファイルを保存
    US->>D: POST /api/download<br/>ダウンロードフォルダの下のパス・メールのメタデータ
    D->>D: パスがダウンロードフォルダの下か確認
    D->>DL: ファイルを読む
    D->>D: rules.yaml で配送先を決定<br/>ZIP なら展開・書き出し
    D->>DB: 配送台帳に記録
    D-->>US: 配送済み
    US-->>U: トースト通知
```

UserScript は Outlook の認証トークンを扱わず、添付の中身を自分では取得しない。添付は Outlook の画面のダウンロードで保存されたものを受け取る。理由は [ADR 002](adr/002-attachments-via-downloads.md) にある。

### PPAP（R-3）: ZIP メールが先に届いた場合

```mermaid
sequenceDiagram
    actor U as 利用者
    participant US as UserScript
    participant D as デーモン
    participant DB as SQLite

    U->>US: ZIP メールを開く
    US->>D: POST /api/process<br/>メタデータ・ZIP・本文
    D->>DB: ペアの相手を探す
    DB-->>D: なし
    D->>DB: 参照を記録<br/>MessageId・添付 ID・SHA-256・HMAC
    Note over D: ZIP と本文をメモリから破棄
    D-->>US: 待機中

    U->>US: パスワードメールを開く
    US->>D: POST /api/process<br/>メタデータ・本文・パスワード候補
    D->>DB: ペアの相手を探す
    DB-->>D: ZIP メールの参照
    Note over D: 本文とパスワードをメモリから破棄
    D-->>US: 再取得の要求<br/>ZIP メールの MessageId・添付
    US->>US: ZIP を MessageId で取得
    US->>D: POST /api/process<br/>パスワードメールの内容と ZIP を一度に
    D->>D: ZIP の SHA-256 を記録と照合
    D->>D: 解凍・rules.yaml で配送先を決定・書き出し
    D->>DB: 配送台帳に記録
    D-->>US: 配送済み
    US-->>U: トースト通知
```

パスワードメールが先に届いた場合は、ZIP とパスワードの役割が入れ替わる。デーモンはパスワードメール本文の再取得を要求し、本文の SHA-256 を照合する。

デーモンはリクエストをまたいで ZIP、本文、パスワードを持たない。ペアが揃ったときは、UserScript が両方の内容を一つのリクエストにまとめて送る。

### ダウンロードリンク（R-4、R-5）

```mermaid
sequenceDiagram
    actor U as 利用者
    participant US as UserScript
    participant L as リンク先
    participant DL as ダウンロードフォルダ
    participant D as デーモン
    participant DB as SQLite

    U->>US: リンクのあるメールを開き、リンクを開く
    U->>L: 必要ならログインし、ダウンロードする
    L->>DL: ファイルを保存
    US->>D: POST /api/download<br/>ダウンロードフォルダの下のパス・メールのメタデータ
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
    participant US as UserScript
    participant D as デーモン
    participant DB as SQLite

    U->>US: メール一覧を表示する
    US->>D: POST /api/status<br/>表示中の MessageId 一覧
    D->>DB: 配送状態を照会
    D-->>US: MessageId ごとの状態
    US-->>U: バッジを表示

    U->>US: バッジをクリック
    US->>D: POST /api/reveal<br/>MessageId
    D->>DB: 配送先パスを照会
    D->>D: エクスプローラーで開く
```

`/api/reveal` はパスを受け取らない。開くのは配送台帳に記録された配送先だけである。

## データの置き場所

| データ | 置き場所 | 期間 |
| --- | --- | --- |
| 本文、パスワード、平文のアドレスとドメイン | デーモンのメモリ | リクエストの処理中だけ |
| 待機中の参照（MessageId、添付 ID、`ConversationId`、受信日時、ZIP と本文の SHA-256、アドレスとドメインの HMAC-SHA256） | SQLite | ペアが揃うか、破棄の期限まで |
| 配送台帳（MessageId、ファイルの SHA-256、配送先パス、ファイル名一覧） | SQLite | 無期限 |
| HMAC の鍵 | 鍵ファイル（DPAPI で保護） | 無期限 |
| 振り分けの規則、送信者の別名 | `rules.yaml` | 利用者が編集する |
| ダウンロードした添付とリンク先のファイル | ダウンロードフォルダ | 配送まで |
| 配送したファイル | 配送先フォルダ | 利用者が管理する |
