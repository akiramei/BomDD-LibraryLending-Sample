# Cheat Report — factory-02-sonnet (forward-01)

製造装置: factory-02 (claude-sonnet-4-6)
製造日: 2026-06-10

## サマリ
- 総件数: 8件
- blocker: 0件
- friction: 2件
- minor: 6件

---

### CHEAT-F01-001 [探索次元] ID 接頭辞以降の形式に GUID (no-hyphen) を選択

- 手法が与えなかったもの:
  K-ID-001 は接頭辞 `bk_` / `mb_` / `ln_` のみを指定し、接頭辞以降は「実装裁量」と明示。
- 代替した判断(何をどう埋めたか):
  `Guid.NewGuid().ToString("n")` — ハイフン無し小文字16進数32文字を使用。
  理由: .NET BCL のみで一意性を保証できる最小の選択。ULID/連番には追加ライブラリまたは競合対策が必要。
  結果例: `bk_4a3b2c1d0e9f8a7b6c5d4e3f2a1b0c9d`
- 重大度: minor (意図的探索次元。選択自体は安全)

---

### CHEAT-F01-002 [探索次元] 応答日時の小数秒ゼロ正規化

- 手法が与えなかったもの:
  仕様 §2.4 は "同一瞬時を表す ISO-8601 literal-Z であればよい" と述べ、小数秒桁を exploratory に置く (probe P-02)。
- 代替した判断(何をどう埋めたか):
  `yyyy-MM-ddTHH:mm:ssZ` 形式 (小数秒なし) に正規化して出力。
  `DateTimeParser.Format()` で一元管理。
  保存時も同形式で SQLite TEXT に格納し、読み取り時に同フォーマットで ParseExact する。
- 重大度: minor (仕様が明示的に exploratory とした次元)

---

### CHEAT-F01-003 [SQLite スキーマ] 日時・日付の TEXT 保存

- 手法が与えなかったもの:
  K-SQLITE-001 は「内部スキーマは自由」と宣言し、型選択を裁量に委ねる。
- 代替した判断(何をどう埋めたか):
  - `LoanedAtUtc` / `ReturnedAtUtc`: `yyyy-MM-ddTHH:mm:ssZ` 形式の TEXT
  - `DueDateUtc`: `yyyy-MM-dd` 形式の TEXT
  SQLite に DATETIME 型は無く、TEXT/REAL/INTEGER の3択。TEXT + 固定フォーマットが読み書き最も明確で、
  外部ツール (sqlitebrowser 等) でも人間可読。
- 重大度: minor (内部スキーマは API 挙動で検査され、形式は自由)

---

### CHEAT-F01-004 [SQLite 設定] WAL モードなし・PRAGMA なし

- 手法が与えなかったもの:
  K-SQLITE-001 は「WAL モード・PRAGMA 類は任意(採用したらずる報告)」と明示。
- 代替した判断(何をどう埋めたか):
  PRAGMA を一切適用しない (デフォルト journal_mode=DELETE)。
  理由: NFR-001 (中央値 300ms) は直列 50 件の計測であり、WAL なしでも達成可能と判断。
  同時書き込み競合 (INV-1) の検査は charter でスコープ外とされているため、WAL の優先度は低い。
- 重大度: minor

---

### CHEAT-F01-005 [API 設計] エラー応答の message 文面方針

- 手法が与えなかったもの:
  K-ERROR-SCHEMA-001 は「message は非空(内容は自由)」と述べ、文言を裁量に委ねる。
- 代替した判断(何をどう埋めたか):
  英語・簡潔な技術的文言を採用。例:
  - `"bookId is required."`
  - `"No copies available."`
  - `"Member has overdue loans."`
  仕様が「言語・内容は検査しない」と明記しているため、日本語/英語の選択は自由とみなし英語で統一。
- 重大度: minor

---

### CHEAT-F01-006 [防御設計] 未ハンドル例外の 500 抑制ミドルウェアの採用

- 手法が与えなかったもの:
  work-order は「未ハンドル例外で 500 を返さない設計とする(防御ミドルウェアの採用は裁量。採用したらずる報告)」と指示。
  BOM には採用/不採用の明示的判断基準がない。
- 代替した判断(何をどう埋めたか):
  `app.UseExceptionHandler(...)` を使用し、未捕捉例外を HTTP 500 + JSON エラー形式で返すよう設定。
  理由: 仕様 §2.8「HTTP 500 は契約違反」と受入仕様「500 発生は fail」を満たすための最低限の防御。
  ただし、500 の `code` 値に `internal_error` を使っており、これは仕様 §2.8 の 6 種列挙外である
  (仕様の「HTTP 500 は契約外」という文言は「そもそも発生させない」と解釈し、500 時の code は
  受入対象外と判断した)。
- 重大度: friction (500 が発生した場合の error.code が仕様列挙外 — ただし 500 自体が契約外)

---

### CHEAT-F01-007 [判定順序] POST /v1/loans の bookId/memberId 欠落時の扱い

- 手法が与えなかったもの:
  仕様 §2.4 判定1は「bookId/memberId の**値の形式**検証はしない」と述べるが、
  フィールド自体の欠落(null)を 400 にするかは文脈から読み取る必要がある。
- 代替した判断(何をどう埋めたか):
  フィールド欠落(null) → 400 `invalid_request`。
  空文字列など「値あり」→ 存在確認(404 側)へ進む。
  根拠: 「欠落・型不正」は 400 と明記 (判定順序1) されており、null はフィールド欠落に相当すると解釈。
  空文字列は「値あり(形式検証不要)」という §2.4 の規則および §2.6 の一貫規則から 404 側と判断。
- 重大度: friction (仕様の「値の形式検証はしない」との境界が微妙)

---

### CHEAT-F01-008 [応答フィールド] 追加フィールドなし方針

- 手法が与えなかったもの:
  K-JSON-001 は「仕様に列挙されたフィールドは必須。追加フィールドは禁止しない(探索次元 P-03)」と述べる。
  M-BOM silence_sweep も「追加フィールド = probe P-03」として探索次元に置く。
- 代替した判断(何をどう埋めたか):
  仕様列挙フィールドのみを出力し、追加フィールドを一切付けない。
  理由: 余分なフィールドを出さない方が受入オラクルの strictness に対してリスクが低い。
  active 貸出の `returnedAtUtc`/`fineAmount` 省略は `DefaultIgnoreCondition.WhenWritingNull` で
  実装したが、匿名型を使い分けることで確実に省略している。
- 重大度: minor (探索次元として意図的)
