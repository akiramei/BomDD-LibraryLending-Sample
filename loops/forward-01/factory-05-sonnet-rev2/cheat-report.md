# Cheat Report — factory-05-sonnet-rev2

製造装置: claude-sonnet-4-6 (factory-05)
製造日: 2026-06-10
入力 BOM rev: forward-01 rev2

---

### CHEAT-F01-001 [C1: 外部知識] グローバル例外ハンドラミドルウェアの採用
- 手法が与えなかったもの: BOM/K-BOM は「500 を返さない設計とする。防御ミドルウェアの採用は裁量。採用したら報告」と記載(40-work-order.md)。採用の判断根拠は BOM から導けない。
- 代替した判断(何をどう埋めたか): `app.Use(async (ctx, next) => { try { await next(ctx); } catch { ... } })` を最外周に配置し、未ハンドル例外が発生した場合でも `{"error":{"code":"internal_error","message":"..."}}` を返すようにした。
- 重大度: minor

---

### CHEAT-F01-002 [C2: 設計判断] 内部スキーマ設計(テーブル・列・型)
- 手法が与えなかったもの: 仕様 §2.7 が「内部スキーマは自由(検査は API 挙動)」と宣言。K-SQLITE-001 もスキーマを指定しない。
- 代替した判断(何をどう埋めたか): Books / Members / Loans の 3 テーブル構成。Loans の Status 列は TEXT('active'/'returned')、ReturnedAtUtc / FineAmount は NULL 許容 TEXT / INTEGER。LoanedAtUtc を TEXT で格納(yyyy-MM-ddTHH:mm:ssZ 形式)し、辞書順ソートで §2.6 瞬時昇順を実現(等価のため)。
- 重大度: minor

---

### CHEAT-F01-003 [C2: 設計判断] SQLite WAL モード・PRAGMA の非採用
- 手法が与えなかったもの: K-SQLITE-001 が「WAL モード・PRAGMA 類は任意(採用したらずる報告)」と記載。採用・非採用の根拠は BOM に無い。
- 代替した判断(何をどう埋めたか): PRAGMA 類は一切設定しない(デフォルト WAL 無効の journal_mode=DELETE のまま)。受入スコープが直列 HTTP テストのため並行書き込み競合は発生せず、WAL の効果は不要と判断した。
- 重大度: minor

---

### CHEAT-F01-004 [C1: 外部知識] エラー message の文面方針
- 手法が与えなかったもの: BOM/K-BOM は「message は非空(内容・言語は検査しない)」と定めるのみ。具体的な英語テキストは与えられていない。
- 代替した判断(何をどう埋めたか): 全エラーメッセージを英語の平文一文に統一した(例: "Book not found.", "No copies available.")。機械可読情報は `code` に集約し、`message` は人間向け補足とした。
- 重大度: minor

---

### CHEAT-F01-005 [C2: 設計判断] L1 スモークテストの API 起動方法(dotnet run + サブプロセス)
- 手法が与えなかったもの: M-BOM は「Library.Api をサブプロセスとして起動」と指定するが、起動コマンド・ポート・待機ポーリング方法は指定しない。
- 代替した判断(何をどう埋めたか): `dotnet run --project <path> --no-build` をサブプロセスとして起動。ASPNETCORE_URLS=http://localhost:5799 でポートを固定。500ms ×最大 30 回ポーリングで起動確認(タイムアウト 15 秒)。
- 重大度: friction

---

### CHEAT-F01-006 [C2: 設計判断] GET /v1/loans 一覧の LoanedAtUtc ソートをテキスト順で代替
- 手法が与えなかったもの: §2.6 は「LoanedAtUtc の瞬時(パース後の時刻値)昇順」を要求。DB 上に DateTimeOffset を直接格納する手段がない(SQLite の TEXT)。
- 代替した判断(何をどう埋めたか): 出力を `yyyy-MM-ddTHH:mm:ssZ`(秒精度・正規化済み)に統一しているため、ISO-8601 テキストの辞書順 = 時刻値順が常に成立する。SQLite ORDER BY LoanedAtUtc ASC で実装。同値時の `id` 序数昇順も ORDER BY Id ASC で追加。
- 重大度: minor (等価性が成立するため実質的影響なし)

---

### CHEAT-F01-007 [C3: 技法不足] Acceptance テスト実行時の作業ディレクトリとプロジェクトパス探索
- 手法が与えなかったもの: M-BOM は `dotnet run --project test/Library.Acceptance` の呼び出し形式を指定するが、サブプロセス内から Library.Api の .csproj を発見する方法は指定しない。
- 代替した判断(何をどう埋めたか): `AppContext.BaseDirectory` から相対パス(5 段上へ)および `Directory.GetCurrentDirectory()` から相対パスの候補リストを順に試し、File.Exists で確認する探索ロジックを実装した。
- 重大度: friction

---

## サマリ

| 件数 | blocker |
|------|---------|
| 7 件 | なし    |

全 7 件はいずれも minor / friction。仕様・K-BOM・Control Plan の矛盾・実装不能は発見されなかった。BOM から一意に導けなかったのは内部スキーマ・PRAGMA・エラーメッセージ文面・例外ミドルウェア採用・起動方法・ソート等価性証明・パス探索の 7 点。
