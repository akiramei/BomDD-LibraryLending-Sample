# cheat-report — ECO-002(factory-eco2-01-opus)

工場個体: factory-eco2-01-opus / baseline: v0.3(factory-eco-01-opus 由来の複製)
実施日: 2026-07-04
ECO: ECO-002 — 会員区分依存の貸出期間(standard=14日 / premium=21日)+効力=貸出作成時点

このレポートは、仕様・BOM が与えていない次元で工場が裁量で決めたこと(全件)、
影響分析(ECO §2)からの逸脱とその理由、自己受入の実行結果を記録する。

---

## 1. 影響分析からの逸脱(under-inclusion の反証データ)

### 1.1 src/Library.Api/Program.cs に diff が出る(ECO §2「Api 層 diff ゼロ」予測の反証)

**ECO §2 の予測**: src/Library.Api(Store.cs / Api.cs / Errors.cs / Ids.cs / Program.cs)は
**ソース diff ゼロ**。根拠は「dueDateText は Core の判定関数が返し、Store/Api は透過に保存・
応答する。LoanContext は ECO-001 以降 MemberType を運んでいる=配線変更も不要」。

**実際の as-maintained(v0.3)コードの構造**:
- dueDate は Core の判定関数(`LendingDecisions.EvaluateLoan`)が返しているのではなく、
  **Program.cs:120 の貸出クロージャ内で `LendingDomain.DueDate(ctx.LoanedAtUtc)` を
  別途呼んで**計算している。
- この呼び出しは `ctx.LoanedAtUtc`(貸出日時)だけを渡しており、`ctx.MemberType` を渡していない。
- 期間を区分依存にするには、`DueDate` が会員区分を受け取る必要がある。会員区分は
  クロージャ内では `ctx.MemberType` としてのみ入手可能。したがって呼び出し側(Program.cs:120)を
  `LendingDomain.DueDate(ctx.LoanedAtUtc, ctx.MemberType)` に変更しなければ、区分依存の
  期限計算は成立しない。

**結論**: 「配線変更も不要」という予測は、実物のコードでは**成立しない**。dueDate 計算は
Core の判定関数の外(Api 層のクロージャ)にあり、そこに MemberType を通す配線が1行必要。
これは ECO §2 の「Api 層 diff ゼロ」予測の**反証**であり、影響分析の **under-inclusion**
(依存があるのに「影響なし」と切ってしまった箇所)として計上されるべき。

**逸脱の性質**: これは工場の裁量ミスではなく、影響分析が実物のコード構造(dueDate を
EvaluateLoan の戻り値ではなく Program.cs の別呼び出しで計算している)を取り違えていたこと
による。変更は最小(1行、引数1つ追加)で、挙動リスク・契約変更はない
(分類: behavior-risk ではなく wiring/配線・1行)。

**回避可能性の検討**:
- 案A(採用): `DueDate(DateTimeOffset, MemberType)` にオーバーロード追加し、Program.cs:120 で
  `ctx.MemberType` を渡す。→ Program.cs 1行の diff。
- 案B(不採用): dueDate 計算を `EvaluateLoan` の戻り値に移す。→ EvaluateLoan の署名変更 +
  Program.cs の呼び出し変更(同じく Api diff かつより大きい)+ Store.cs の戻り値配線変更まで
  波及。影響が拡大するため不採用。
- **どちらの案でも Api 層の diff はゼロにできない**。dueDate 計算式が会員区分を必要とし、
  会員区分が Api 層のクロージャ経由でしか流れないため。案A を「影響最小」として採用した。

### 1.2 影響あり箇所は予測どおり

- E-DUE-FINE-001 / M-CORE-LENDING-001(期限計算)= src/Library.Core/LendingDomain.cs のみ改修。
- 延滞判定・料金・上限・API 契約・Store.cs は不変(効力は保存済み due_date_utc を読むだけで
  自動追従する構造を確認済み: return 経路は Store.cs が保存した DueDateUtc を再パースし
  Program.cs:163-164 で fine 計算、overdue は Store.cs:280 が保存済み due_date_utc を読む)。

---

## 2. 裁量で決めたこと(仕様・BOM が与えていない次元)

- **premium 定数の命名**: `LoanPeriodPremiumDays = 21`(既存 `LoanPeriodDays = 14` は
  `LoanPeriodStandardDays` にリネームせず、標準の定数として残すと ECO 無関係のリネーム diff が
  出るため、既存名を standard 用に温存し premium 定数のみ新規追加した)。仕様は日数値(14/21)の
  みを与え、定数名は与えていない。
- **DueDate の署名変更方式**: 既存の `DueDate(DateTimeOffset)` を残さず、
  `DueDate(DateTimeOffset, MemberType)` に**置き換え**た(オーバーロードで両方残すと未使用の
  旧署名が残り、テストヘルパも旧署名を呼び続けられてしまうため。呼び出し側は Program.cs:120 と
  test の Due ヘルパの2箇所のみで、両方を新署名に更新した)。仕様は署名を規定しない。
- **効力の実装手段**: 「効力=作成時点で確定・遡及しない」は、dueDate を**貸出行に保存し
  (既存)、延滞判定・料金は保存済み値を読むだけ**という既存構造で自動的に満たされる。
  期間日数(14/21)を判定側で再導出するコードは**追加しなかった**(FMEA-006 の失敗モードを
  作り込まない)。これは裁量というより「既存構造がそのまま効力を保証する」ことの確認。

---

## 3. 自己受入の実行結果

### 3.1 ビルド(`dotnet build Library.sln -c Release`)
- **ビルド成功・0 エラー**。
- **警告 = 4 件、ただし全て NU1903(NuGet 脆弱性アドバイザリ)** で、対象は
  `SQLitePCLRaw.lib.e_sqlite3` 2.1.10 — `Microsoft.Data.Sqlite 9.0.0`(32-mbom.yaml で
  substitutable:false・10.x への更新は別 ECO と明記)の**推移的依存**。
  - コンパイラ(CS)警告は **0**。この NU1903 は改修前の baseline にも存在する環境ノイズであり、
    ECO-002 の改修が新たに導入したものではない(.csproj / PackageReference は一切変更していない)。
  - パッケージを更新して警告を消すことは **ECO-002 のスコープ外の変更**(procurement 固定を
    破る=ずる)になるため、**あえて変更しなかった**。自己受入の「警告 0」はコード改修由来の
    警告 0(=CS 警告 0)として満たされていると判断する。逸脱判断として記録する。

### 3.2 受入ハーネス(unit + L1 API スモーク)
- **RESULT: 37 passed, 0 failed(全緑)**。内訳:
  - unit(CP-CORE-* を Library.Core 直接呼び): **31 passed**
    - うち CP-CORE-DUE-001 = **6 本**(standard 3 + premium 3。premium 3 本は今回追加)。
  - L1 API スモーク(6 エンドポイント・サブプロセス起動): **6 passed**。
- **スモーク実行上の注意(検査独立性のため記録)**: 受入ハーネスは API を
  `dotnet run --project ... --no-build`(**構成無指定=既定 Debug**)でサブプロセス起動する。
  そのため `-c Release` だけをビルドした状態では Debug バイナリが存在せず、スモークが
  「API process became ready」で FAIL する(実測: Release のみビルド→ 31 passed / 1 failed)。
  `dotnet build Library.sln`(Debug)も併せてビルドしてから走らせると **37/37 全緑**。
  この挙動は既存ハーネスの構成不一致であり、ECO-002 の改修とは無関係
  (ハーネス・Api 起動・ビルド構成には一切触れていない)。最終の受入判定は Debug バイナリ
  存在下の **37 passed / 0 failed** を採る。

### 3.3 premium 3 ベクタ(CP-CORE-DUE-001 rev4)の追加内容
test/Library.Acceptance/UnitChecks.cs の DueCheck に以下を追加(全 PASS):
- premium 2026-01-31T10:00:00Z → 2026-02-21(月またぎ)
- premium 2026-12-25T00:00:00Z → 2027-01-15(年またぎ)
- premium 2026-06-10T23:59:59Z → 2026-07-01(時刻無関係・月末跨ぎ)

**自己受入判定: 緑(赤なし)。納品可。**

---

## 4. 変更ファイル一覧(git diff 基準点=複製時点)
- `src/Library.Core/LendingDomain.cs`(改修対象・E-DUE-FINE-001 / M-CORE-LENDING-001):
  `LoanPeriodPremiumDays=21` 追加 / `LoanPeriod(MemberType)` 追加 /
  `DueDate` を `DueDate(DateTimeOffset, MemberType)` に置換(区分依存+21日)。
- `src/Library.Api/Program.cs`(**影響分析外の diff・§1.1 に理由記録**):
  L120 の `DueDate(ctx.LoanedAtUtc)` → `DueDate(ctx.LoanedAtUtc, ctx.MemberType)`(1行・配線のみ)。
- `test/Library.Acceptance/UnitChecks.cs`(test-only・M-ACCEPTANCE-HARNESS-001):
  `Due` ヘルパに MemberType 引数追加 / CP-CORE-DUE-001 に premium 3 ベクタ追加。
- `cheat-report.md`(新規・本ファイル)。

Store.cs / Api.cs / Errors.cs / Ids.cs は不変(効力は保存済み due_date_utc の読み出しで自動追従)。
