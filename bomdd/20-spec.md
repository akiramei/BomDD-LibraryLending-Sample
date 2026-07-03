# 仕様書 — Library Lending API(forward-01, rev1 = G2 監査反映版)

> 製造パッケージに含まれる。REQ への双方向トレースを保つ(§5)。
> rev1: マルチリーダー監査(G2)が検出した曖昧箇所を固定した(ゲート記録参照)。
> rev4(ECO-002・2026-07-04): 貸出期間を会員区分依存に(standard=14日 / premium=21日)+効力=貸出作成時点(§2.4)。REQ-003 rev2。

## 1. 概要と用語
図書館の蔵書貸出を管理する HTTP API。**蔵書(book)**は部数(copies)を持つ。**会員(member)**は蔵書を**貸出(loan)**し、**返却(return)**する。返却されていない貸出を **active loan** と呼ぶ。

- **日時形式(入力)**: `yyyy-MM-ddTHH:mm:ssZ` または小数秒付き `yyyy-MM-ddTHH:mm:ss.fZ`〜`.fffffffZ`(小数秒 1〜7 桁)。**大文字 `Z` のみ**受理する。それ以外 — 小文字 `z`、数値オフセット(`+09:00`、**`+00:00` を含む**)、オフセット無し、日付のみ — はすべて **400 `invalid_request`**。
- **日時形式(出力・rev2 で固定)**: 応答の日時は **`yyyy-MM-ddTHH:mm:ssZ`(秒精度・小数秒なし)** に正規化する。小数秒付き入力は受理するが、秒未満を**切り捨て**て保存・応答する(ドメイン判定は秒未満を使わない)。`dueDateUtc` は `yyyy-MM-dd` の 10 文字。
- **サーバ時計は一切使わない**: 時刻に依存する判定はすべてリクエストが運ぶ日時(loanedAtUtc / returnedAtUtc)だけで行う。**未来の日時も受理する**(窓口の後入力ユースケース)。
- **UTC 暦日(date part)**: 日時の `yyyy-MM-dd` 部分。期限・延滞の判定は UTC 暦日の比較で行う。
- **暦日差**: 2 つの暦日を日付として引いた日数(例 `2026-02-15 − 2026-02-14 = 1`)。
- 通貨は整数円。小数は発生しない。
- 文字数は .NET の `string.Length`(UTF-16 コードユニット数)。前後空白のトリムはしない(空白のみでも長さを満たせば受理)。
- **スコープ外(意図的省略)**: 認証・冪等キー・ページング・蔵書/会員の更新/削除/一覧・会員単体取得・**会員区分の変更手段(登録時のみ。rev3)**。一覧応答は常に全件。

## 2. 機能仕様

### 2.1 蔵書登録 — POST /v1/books (REQ-006)
- リクエスト: `{ "title": string(1..200), "copies": integer(1..100) }`
- 成功: **201** `{ "id": string, "title": string, "copies": integer, "availableCopies": integer }`(登録直後は availableCopies == copies)
- 検証エラー(欠落・範囲外・型不正): **400** `code=invalid_request`

### 2.2 蔵書取得 — GET /v1/books/{id} (REQ-001, REQ-006)
- 成功: **200**(2.1 と同形。availableCopies = copies − active loan 数)
- 不在: **404** `code=not_found`

### 2.3 会員登録 — POST /v1/members (REQ-006, REQ-008)
- リクエスト: `{ "name": string(1..100), "memberType": "standard" | "premium"(任意。既定 "standard") }`
- 成功: **201** `{ "id": string, "name": string, "memberType": string }`
- 検証エラー(name 不正・memberType が列挙外の値): **400** `code=invalid_request`

### 2.4 貸出 — POST /v1/loans (REQ-001, REQ-002, REQ-004, REQ-006)
- リクエスト: `{ "bookId": string, "memberId": string, "loanedAtUtc": 日時(§1) }`
- 成功: **201** `{ "id": string, "bookId": string, "memberId": string, "loanedAtUtc": string, "dueDateUtc": "yyyy-MM-dd", "status": "active" }`
- **判定順序(上から評価し、最初に該当したエラーを返す)**:
  1. 入力検証(フィールド欠落・型不正・日時形式違反)→ **400** `invalid_request`。bookId/memberId は**非空の文字列**であること(欠落・null・非文字列・**空文字・空白のみ** → 400。rev2 で固定: 「存在しない ID」と「入力不正」を混ぜない)。それ以外の値の形式検証はせず、次段の存在確認へ。
  2. bookId / memberId 不在 → **404** `not_found`(両方不在でも code は同じ `not_found`)
  3. 会員が延滞中 → **409** `member_overdue_blocked`。**延滞中** = その会員の active loan の**いずれか 1 件でも** `UTC暦日(loanedAtUtc) > dueDateUtc`(INV-3)
  4. 会員の active loan が区分上限(standard=3 / premium=5)に達している(INV-2 rev3)→ **409** `loan_limit_exceeded`
  5. 蔵書の availableCopies == 0(INV-1)→ **409** `no_copies_available`
- `dueDateUtc` = UTC暦日(loanedAtUtc) + **区分依存日数**(rev4/ECO-002: standard = **14日** / premium = **21日**。暦日加算。standard 例 `1/31 → 2/14`、premium 例 `1/31 → 2/21`・`12/25 → 翌年 1/15`)。**`yyyy-MM-dd` の 10 文字文字列**(時刻部なし)。
- **効力(rev4/ECO-002)**: `dueDateUtc` は**貸出作成時に確定**し、以後の規則・区分の変更に追従しない。既存貸出(本改訂前に作成)の期限は従来の値のまま(遡及しない)。延滞判定(§2.4-3)・延滞料金(§2.5)は常に**その貸出の確定済み dueDateUtc** を基準にする(期間日数を判定側で再計算しない)。
- `loanedAtUtc`(応答)= 入力と同一瞬時(秒未満切り捨て)を **`yyyy-MM-ddTHH:mm:ssZ` 固定形式**で返す(§1 出力形式。rev2 で固定)。

### 2.5 返却 — POST /v1/loans/{id}/return (REQ-003, REQ-007)
- リクエスト: `{ "returnedAtUtc": 日時(§1) }`
- 成功: **200** `{ "id", "bookId", "memberId", "loanedAtUtc", "dueDateUtc", "status": "returned", "returnedAtUtc": string, "fineAmount": integer }`
- `fineAmount` = `max(0, 暦日差(UTC暦日(returnedAtUtc) − dueDateUtc)) × 100`。
  - 期限日当日の返却(時刻不問、23:59:59Z でも)= **0**。期限日翌日 00:00:00Z = **100**。早期返却(期限前)= **0**。
- **判定順序(上から評価し、最初に該当したエラーを返す)**:
  1. 入力検証(欠落・型不正・日時形式違反)→ **400** `invalid_request`
  2. 貸出 {id} 不在 → **404** `not_found`
  3. 返却済み(status=returned)→ **409** `already_returned`(同一 returnedAtUtc の再送でも 409。冪等化しない)
  4. `returnedAtUtc < loanedAtUtc`(**瞬時=時刻の比較**。暦日ではない)→ **400** `invalid_request`。同一瞬時は受理(fine は暦日で計算)。

### 2.6 会員別貸出一覧 — GET /v1/loans?memberId={id} (REQ-006)
- 成功: **200** `{ "items": [ 貸出オブジェクト ] }`(全件。ページングなし)
- **並び順**: `loanedAtUtc` の**瞬時(パース後の時刻値)昇順**。同一瞬時は `id` の**序数(ordinal)文字列比較**で昇順。
- **オブジェクト形**: active の貸出は 2.4 成功形(`returnedAtUtc`・`fineAmount` を**含めない**)。returned の貸出は 2.5 成功形(両フィールドを含む)。
- memberId 欠落(クエリパラメータ自体が無い)・**空文字・空白のみ(`?memberId=`)** → **400** `invalid_request`(rev2 で §2.4 判定1 と同じ「非空文字列」規則に統一)/ memberId 不在 → **404** `not_found`。
- active と returned の両方を含む。

### 2.7 永続化・スキーマ移行 (REQ-005, REQ-009)
- 全データはプロセス終了後も保持され、再起動後に同じ API で取得できる。
- 保存先: SQLite 単一ファイル。パスは環境変数 `LIBRARY_DB_PATH` で指定(未設定時は `./library.db`)。相対パスはプロセスの作業ディレクトリ基準。親ディレクトリは存在する前提でよい(無い場合の挙動は規定しない)。
- 起動時にファイルが無ければスキーマごと作成する。
- **スキーマ移行(rev3, REQ-009)**: 起動時に v0.2 スキーマ(As-Maintained 個体=factory-04 の内部スキーマ)の DB を検出した場合、自動移行する。既存データ(蔵書・会員・貸出・返却状態・延滞料金)をすべて保持し、既存会員の memberType は `standard` とする。起動失敗・データ消失をしない(ロールバックは不要)。
- 内部スキーマ(テーブル名・列名・型)は仕様で固定しない(検査は挙動=API で行う)。移行の正否も挙動(専用オラクル M01–M04)で検査する。

### 2.8 エラー応答スキーマ(全エンドポイント共通)
```json
{ "error": { "code": "<machine-readable>", "message": "<human-readable>" } }
```
- **code の全列挙**(これ以外を返さない): `invalid_request` / `not_found` / `no_copies_available` / `loan_limit_exceeded` / `member_overdue_blocked` / `already_returned` / **`internal_error`**(rev2 で追加: サーバ内部エラー用の共通語彙。4xx 業務語彙とは別)
- `message` は非空文字列。内容・言語は検査しない。
- HTTP 500 が発生した場合も応答は共通封筒 + `code=internal_error` とする。ただし**受入では 500 の発生自体が fail**(固定オラクルは 500 を積極的に注入・誘発しない)。

## 3. 不変条件
| ID | 不変条件 | REQ |
|---|---|---|
| INV-1 | 蔵書ごとに active loan 数 ≤ copies(貸出判定と作成は原子的に行い、同時要求でも超過しない) | REQ-001 |
| INV-2 | 会員ごとに active loan 数 ≤ 区分上限(standard=3 / premium=5。rev3) | REQ-002, REQ-008 |
| INV-3 | 延滞判定は UTC 暦日比較: 「延滞中」= active loan のいずれかが `UTC暦日(基準時刻) > dueDateUtc`。基準時刻は操作のリクエストが運ぶ(新規貸出時 = その loanedAtUtc)。サーバ時計は使わない | REQ-003, REQ-004 |
| INV-4 | 貸出の状態機械は active → returned の一方向のみ。returned からの遷移は無い | REQ-007 |
| INV-5 | ID = 接頭辞(蔵書 `bk_`・会員 `mb_`・貸出 `ln_`)+ **32桁の小文字16進数**(GUID "N" 形式。rev2 で固定: 公開契約のため)。並び順の意味は ID に依存させない(§2.6 の第一キーは loanedAtUtc) | REQ-006 |

## 4. 沈黙次元の第1回掃討(silence-checklist)
| 次元 | 宣言 | 内容/参照 |
|---|---|---|
| 日時表現 | specified | §1(大文字Z のみ・小数秒 0〜7 桁・`+00:00` も拒否) |
| 暦日・期限の境界 | specified | §1/§2.5(当日=0・翌日=100・早期=0・瞬時比較は §2.5-4 のみ) |
| 丸め | specified | 整数演算のみ(§1) |
| エラー語彙 | specified | §2.8 全列挙+message 非空+500 は契約外 |
| status code 対応 | specified | §2.4/2.5 判定順序込み(両節とも「上から評価」明記) |
| ID | specified(rev2 で昇格) | 接頭辞+32桁小文字hex(INV-5)。forward-01 探索層の分散をユーザー裁定で固定 |
| 一覧順序 | specified | 瞬時昇順+id 序数昇順(§2.6) |
| 応答フィールド集合 | specified(一部 deferred) | 列挙フィールドは必須。active は returnedAtUtc/fineAmount を含めない(§2.6)。**追加フィールドの可否**は M-BOM で宣言 |
| 応答日時の小数秒 | specified(rev2 で昇格) | 出力は秒精度固定・小数秒なし(§1)。入力は 0〜7 桁許容のまま |
| 未来日時 | specified | 受理(§1 サーバ時計不使用) |
| 永続化(配置) | specified | LIBRARY_DB_PATH(§2.7) |
| 内部スキーマ | out-of-scope | 挙動で検査(§2.7) |
| 同時更新の競合 | specified(検査は次段) | INV-1。固定オラクルでは検査しない(charter) |
| 調達部品 | deferred-to-phase3 | M-BOM procurement で固定 |
| 会員区分(rev3) | specified | 列挙2値・既定 standard・不正値 400(§2.3)。新エラーコードは増やさない |
| スキーマ移行(rev3) | specified | §2.7(自動移行・データ保持・既存=standard)。検査は専用オラクル M01–M04 |

## 5. トレース表
| REQ | 実現節 | 受入観点(深さ) |
|---|---|---|
| REQ-001 | §2.1/2.2/2.4 INV-1 | unit + L3(HTTP) |
| REQ-002 | §2.4 INV-2 | unit + L3 |
| REQ-003 | §2.4 dueDate / §2.5 fine, INV-3 | unit(境界ベクタ必須)+ L3 |
| REQ-004 | §2.4 判定3, INV-3 | unit + L3 |
| REQ-005 | §2.7 | **L3(再起動 execution)** |
| REQ-006 | §2 全節 | L2(契約)+ L3 |
| REQ-007 | §2.5, INV-4 | unit + L3 |
| NFR-001 | §2.4(REQ-006 が提供する貸出操作に付随する品質目標) | L3(計測: 直列50件の中央値 < 300ms) |

---
## ゲート記録(G2/G2')
- **G2 マルチリーダー監査(実施 2026-06-10)**: リーダー3体(opus/sonnet/haiku、互いに非開示・仕様のみ供与)。要求・不変条件の抽出は 3 体とも一致(振る舞いは一意に読めた)。**曖昧指摘: opus 17 / sonnet 13 / haiku 17 件**。3 体が独立に重ねた指摘(=最優先で固定): `+00:00` の扱い / ISO 許容形(小文字z・小数秒)/ 返却過去判定の粒度(暦日 vs 瞬時)/ 一覧 id 昇順の定義(INV-5 裁量との不整合)/ 延滞 any/all / 一覧の「同形」(active の returnedAtUtc/fineAmount)。単独だが採用: 未来日時の受理(sonnet)/ 500 の応答形は契約外(opus)/ 同一 returnedAtUtc 再送も 409(haiku)/ message 非空(sonnet)。**Loop7 の「振る舞いは一致し精度で割れる」がフォワード仕様でも再現**(割れたのは期待値の精度次元)。本 rev1 で全て固定または明示的に exploratory/out-of-scope 化。再監査は省略し G3 ドライランで代替(コスト判断。差分は手法ずれとして cheat-log に記録)。
- **G2' MeasurementCapability**: 全 REQ/NFR = adequate(HTTP 黒箱+再起動 execution+L3 計測で観測可能)。例外: **INV-1 の同時要求の原子性 = insufficient-depth**(並行検査治具が無い。charter でスコープ外宣言済み、次段へ)。unmeasurable なし。承認者必要なし(知覚特性なし)。
- **rev3(2026-06-10, ECO-001 = forward-01.5)**: 会員区分の導入(REQ-002 改訂・REQ-008/009 新規)。変更の典拠と影響分析は [60-change-order-eco-001.md](60-change-order-eco-001.md)。S01–S23 は不変、追加 S24–S25+移行オラクル M01–M04。
- **rev2(2026-06-10, Phase 5 ユーザー裁定による仕様昇格)**: Q1 ID 形式=接頭辞+32桁小文字hex / Q2 応答日時=秒精度固定 / Q3 null・欠落・空文字・空白=400 に統一 / Q4 `internal_error` を共通語彙として列挙に追加。オラクルへの昇格は**ループ境界で実施**(v2 = S01–S23、tag `forward-01-rev2-input-bom` で再凍結)。Q5(haiku)=工場能力として記録のみ、再試行は別測定 `haiku-retry` 扱い。
