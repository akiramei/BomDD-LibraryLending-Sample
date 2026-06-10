# Cheat Report — ECO-001 (factory-eco-01-opus)

BOM/ECO から一意に導けなかった判断を全件記録する(work order 様式)。
実装は止めず、いずれも friction/minor 級。blocker なし。

### CHEAT-F015-O001 [SQLite/migration] スキーマ版管理に user_version PRAGMA を選択
- 手法が与えなかったもの: K-SQLITE-001 は「schema バージョンを DB 内で管理(user_version PRAGMA か version テーブル。選択は裁量・ずる報告)」とし、二択を明示的に裁量に委ねている。
- 代替した判断: `PRAGMA user_version` を採用(version テーブルを作らない最小手段)。v0.2 個体は user_version=0、rev3 を 1 とした。起動時に現バージョン < 1 なら `members` に member_type 列が無い場合のみ `ALTER TABLE ADD COLUMN` し、user_version を 1 に進める。CREATE→DROP は使わず既存行を保持。
- 重大度: minor(K-BOM が明示的に裁量化した二択)

### CHEAT-F015-O002 [SQLite/schema] member_type の格納と既定値
- 手法が与えなかったもの: 内部スキーマ(列名・型・既定値)は仕様§2.7/E-SQLITE-STORE-001 で「自由(検査は API 挙動)」。member_type 列の具体形は未規定。
- 代替した判断: `members.member_type TEXT NOT NULL DEFAULT 'standard'`。値は仕様の wire 値("standard"/"premium")をそのまま格納。ALTER の DEFAULT 'standard' が既存行を standard にする(REQ-009「既存会員は standard」を移行と新規挿入の両方で満たす)。
- 重大度: minor(内部スキーマは挙動契約に出ない)

### CHEAT-F015-O003 [migration] フレッシュ DB と移行の統一実装
- 手法が与えなかったもの: 新規作成 DB と v0.2 DB の両方を起動時に正しく整えるフローの具体。
- 代替した判断: EnsureSchema は (a) CREATE TABLE IF NOT EXISTS(members は member_type 込み)で新規 DB を作り、(b) その後 Migrate() を必ず呼ぶ。フレッシュ DB は (a) で既に member_type を持つため Migrate の ALTER は `ColumnExists` ガードで no-op になり、user_version だけが 1 に進む。v0.2 DB は (a) が全 no-op、(b) の ALTER で member_type を追加。両経路とも最終的に version=1・member_type 有りに収束する。
- 重大度: minor

### CHEAT-F015-O004 [API/validation] memberType の型不正 vs 列挙外の切り分け
- 手法が与えなかったもの: 仕様§2.3 は「memberType が列挙外の値 → 400 invalid_request」「任意・既定 standard」とするが、memberType が JSON 文字列でない型(数値・bool 等)で来た場合の扱いは明記なし。
- 代替した判断: プロパティ非存在 → 既定 standard(201)。プロパティ有りだが非文字列 → 400。文字列だが列挙外("gold" 等)→ 400。既存の入力検証規律(K-HTTP-REST-001: 検証エラーは一律 400 invalid_request)に揃えた。新エラーコードは追加していない。
- 重大度: minor

### CHEAT-F015-O005 [test] CP-CORE-LIMIT-001「返却済みは数えない」の被覆方法
- 手法が与えなかったもの: CP-CORE-LIMIT-001 の test_vector「返却済みは数えない(上限到達後に1返却→新規成功)」を unit 粒度(Library.Core 直呼び)でどう表すか。Core は active 貸出の due 日付リストしか受け取らず、返却済みはそもそもリストに含まれない構造。
- 代替した判断: active 件数がリスト長で表現される設計を前提に、「リスト長が上限未満なら Allowed」を standard(2件→Allowed)で確認することで「返却で枠が空く」性質を被覆。返却そのものの状態遷移は既存 CP-CORE-AVAIL / L1 smoke / 設計者側 L3 が担う。
- 重大度: minor

### CHEAT-F015-O006 [test] 移行の自己検証は納品物に含めない判断
- 手法が与えなかったもの: REQ-009 の移行自己検証方法は裁量(「旧コードで DB を作って新コードで開く」例示)。CP-MIGRATION-001 は L3 で設計者側オラクル(migration-oracle.ps1 + fixture)が本検査を担い、その実装・fixture は工場に非開示。
- 代替した判断: 作業ディレクトリ外への影響を避けるため、v0.2 スキーマ DB を手で組み実 Store.EnsureSchema()/CreateLoanAtomic() を叩く使い捨て検証プロジェクト(_migcheck)を一時作成し、M01–M04 相当(起動・データ保持・既存=standard・既存 active が上限カウント・fine 保持・冪等)10 件 PASS を確認後、プロジェクトごと削除した。納品 diff には残さない(test-only の不要改変を避ける)。
- 重大度: minor(検査方法の裁量。納品物には痕跡なし)
