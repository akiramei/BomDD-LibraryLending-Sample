# Cheat Report — forward-01 / factory-01-opus

製造装置: factory-01 (opus)
対象: Library Lending API(.NET 10 minimal API + SQLite / Microsoft.Data.Sqlite 9.0.0)
自己受入: `dotnet build Library.sln` 成功 / `dotnet run --project test/Library.Acceptance` = 24 PASS / 0 FAIL / exit 0

BOM・K-BOM・Control Plan から一意に導けなかった判断を全件記録する。重大度: blocker / friction / minor。
**blocker は 0 件**(BOM の自己矛盾・実装不能なし)。

---

### CHEAT-F01-001 [ID形式] 接頭辞以降は GUID("N" 32桁 hex 小文字)を採用
- 手法が与えなかったもの: K-ID-001 / INV-5 は接頭辞(bk_/mb_/ln_)のみ固定し、接頭辞以降の形式(GUID/hex/連番/ULID風)を「実装裁量・探索次元」と明示。
- 代替した判断: `<prefix>` + `Guid.NewGuid().ToString("N")`(32桁の小文字 hex)を採用。例 `bk_48cb9d28778344b2977e670007e987c7`。BCL のみで一意・追加パッケージ不要(K-SQLITE-001 の「追加パッケージ禁止」と整合)。§2.6 の id 序数(ordinal)昇順比較は固定長 hex で安定して定まる。
- 重大度: minor(探索次元として明示済み)

### CHEAT-F01-002 [応答日時の小数秒・正規化] 応答 literal-Z を秒精度に正規化
- 手法が与えなかったもの: §2.4 は「echo を要求しない・同一瞬時の literal-Z であればよい・小数秒の桁は実装裁量(探索次元 P-02)」。応答の桁数を固定していない。
- 代替した判断: 応答・保存ともに `yyyy-MM-ddTHH:mm:ssZ`(秒精度、小数秒なし)へ正規化(`Iso.Format`)。入力で `.123Z` を受理しても応答は `...:00Z`。瞬時の同値性は保たれる(本題材のドメイン判定はすべて秒以下を使わない)。dueDateUtc は仕様通り `yyyy-MM-dd` の 10 文字。
- 重大度: minor(探索次元。echo 不要が明示)

### CHEAT-F01-003 [応答フィールド集合] 仕様列挙外の追加フィールドは出さない
- 手法が与えなかったもの: K-JSON-001 は「追加フィールドは禁止しない(探索次元 P-03)」。出す/出さないを固定していない。
- 代替した判断: 仕様 §2 に列挙されたフィールドのみ出力し、追加フィールドは付けない(最小)。active の貸出は `returnedAtUtc`/`fineAmount` を省略、returned は両方を含む(K-JSON-001 の必須挙動は遵守)。
- 重大度: minor(探索次元として明示済み)

### CHEAT-F01-004 [SQLite内部スキーマ] テーブル/列の設計を選択
- 手法が与えなかったもの: 仕様 §2.7 / E-SQLITE-STORE-001 は「内部スキーマ(テーブル名・列名・型)は自由。検査は API 挙動」。
- 代替した判断: 3 テーブル `books(id,title,copies)` / `members(id,name)` / `loans(id,book_id,member_id,loaned_at_utc,due_date_utc,status,returned_at_utc,fine_amount)`。日時は literal-Z 文字列、dueDate は `yyyy-MM-dd` 文字列で保存。member/book ごとの絞り込み用に補助 INDEX を作成。一覧の並び(§2.6 瞬時昇順)は SQL の文字列 ORDER BY ではなく**読み出し後に DateTimeOffset で比較**してソート(秒精度正規化済みなので文字列順=瞬時順だが、意味的に正しい瞬時比較を明示採用)。
- 重大度: minor(out-of-scope 宣言済み次元)

### CHEAT-F01-005 [防御ミドルウェア] 未ハンドル例外の 500 回避ミドルウェアを採用
- 手法が与えなかったもの: Work Order は「未ハンドル例外で 500 を返さない設計とする。防御ミドルウェアの採用は裁量。採用したらずる報告」。
- 代替した判断: パイプライン先頭に try/catch ミドルウェアを置き、未ハンドル例外時は(応答未開始なら)`400 invalid_request` の共通封筒へ潰す。K-HTTP-REST-001「500 は契約違反」を保険として担保。通常経路は例外を投げない設計(入力検証で先に弾く)であり、これはセーフティネット。
- 重大度: friction(契約列挙の 500 を避けるための裁量実装。正常系では発火しない)

### CHEAT-F01-006 [SQLiteトランザクション/PRAGMA] 書き込みを1トランザクション・PRAGMA非採用
- 手法が与えなかったもの: K-SQLITE-001 は「書き込み=判定と更新を1トランザクション」「WAL・PRAGMA は任意。採用したらずる報告」「接続は操作ごとに開閉」。具体的な分離レベル・PRAGMA は未指定。
- 代替した判断: 貸出作成・返却は `BeginTransaction()` 内で存在確認・在庫/上限/延滞判定・INSERT/UPDATE を行い Commit(INV-1/INV-2 の原子性)。接続は操作ごとに `using` で開閉。**WAL・PRAGMA 類は一切採用せず既定のまま**(挙動契約に影響しないため最小構成)。プールは既定に委譲。
- 重大度: minor(裁量と明示。PRAGMA 不採用 = 既定)

### CHEAT-F01-007 [エラーmessage文面] message を固定の英短文に
- 手法が与えなかったもの: §2.8 / K-ERROR-SCHEMA-001 は「message は非空。内容・言語は検査しない」。文面は未指定。
- 代替した判断: 各エラーに固定の英語短文を付与(例 "no copies available")。非空のみが契約要件なので機械可読な `code` を真とし、`message` は人間向け補助として最小限に固定。
- 重大度: minor(検査対象外)

### CHEAT-F01-008 [入力型検証の厳密度] JSON 型の弾き方を規定
- 手法が与えなかったもの: §2.4 判定1「フィールド欠落・型不正・日時形式違反 → invalid_request」。ただし JSON の型不正(copies に文字列/真偽値/小数が来た等)の細目は仕様に逐一は無い。
- 代替した判断: `copies` は JSON number かつ整数のみ受理(小数・真偽・文字列・null・欠落は 400)。`title`/`name` は JSON string のみ(null・非文字列は 400、空文字は長さ検証で 400)。bookId/memberId/日時フィールドは JSON string のみ(値の形式検証はせず、空文字も「値あり」として存在確認=404 へ送る。§2.4 判定1・§2.6 の規則と一貫)。ボディが JSON オブジェクトでない/壊れている場合も 400 invalid_request。
- 重大度: friction(型不正の網羅的な期待値は仕様に明記されず、慣習的に最も厳密側へ倒した)

### CHEAT-F01-009 [文字数カウント] 長さ判定は string.Length(UTF-16コードユニット)
- 手法が与えなかったもの: §1 が「文字数は .NET の string.Length / トリムしない」と明示しているため厳密にはずるではないが、サロゲートペア等の境界は仕様に例が無い。
- 代替した判断: 仕様 §1 の文言どおり `string.Length`(UTF-16 コードユニット数)で 1..200 / 1..100 を判定。前後空白トリムなし。
- 重大度: minor(仕様準拠の確認的記録)

---

## 自己受入で被覆した Control Plan unit 行
- CP-CORE-AVAIL-001 / CP-CORE-LIMIT-001 / CP-CORE-DUE-001 / CP-CORE-FINE-001 / CP-CORE-OVERDUE-001 の全 test_vectors(計24チェック、Library.Core を直接呼ぶ)。
- 加えて §2.4 判定順序(延滞 > 上限 > 在庫)の補強チェックを2件。

## L3 自己被覆(任意=M-BOM scope)について
自己受入ハーネス本体には L3 を含めていない(M-BOM 必須範囲外)。製造装置側の手動スモークで以下を確認済み(納品物には含めない):
HTTP 契約(201/200/400/404/409・エラー封筒)/ 日時ポリシー(+00:00・小文字z・日付のみ=400、小数秒Z=受理)/ active の returnedAtUtc・fineAmount 省略 / returned の両フィールド含有 / fine=300 の算定 / 空文字 memberId=404・クエリ無し=400 / **再起動後の永続性(プロセス kill→再起動→GET で同値、FMEA-003)**。
これらの正式判定は設計者側の固定オラクル(oracle/fixed-oracle.ps1)が行う。
