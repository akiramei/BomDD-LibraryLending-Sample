# Cheat Report — ECO-002 (forward-02, factory-eco2-02-sonnet)

対象: 60-change-order-eco-002.md の適用(会員区分依存の貸出期間 + 効力=貸出作成時点)。
本レポートは工場個体 factory-eco2-02-sonnet の製造記録。読んだのは work order 冒頭に列挙された製造パッケージのみ
(ECO-002 本文 + 10/20/30-34/40)。oracle/・41-fixed-oracle 以降・旧 cheat-report・他工場成果は未読。

## 変更したファイル(3件、すべて影響分析の予測範囲内)

### 1. src/Library.Core/LendingDomain.cs (M-CORE-LENDING-001)
- `LoanPeriodDays`(定数14)を `LoanPeriodDaysStandard=14` / `LoanPeriodDaysPremium=21` に分離。
- `LoanPeriodDays(MemberType)` を新設(`LoanLimit(MemberType)` と対の形。既存の命名パターンに倣った)。
- `DueDate(DateTimeOffset loanedAtUtc)` のシグネチャを `DueDate(DateTimeOffset loanedAtUtc, MemberType memberType)` に変更し、区分依存日数を加算するようにした。
- 効力(遡及なし)はこの関数を**貸出作成時のみ呼ぶ**ことで担保する(呼び出し側は Program.cs の1箇所のみ)。延滞判定(`IsMemberOverdue`)・料金計算(`Fine`)は元から確定済み `DateOnly dueDate` を引数に取る形で、期間日数を再計算しないため無改修(ECO §2 の予測どおり)。

### 2. src/Library.Api/Program.cs (M-API-SQLITE-001 — 影響分析は「diff ゼロ」予測)
- 1行のみ変更: `LendingDomain.DueDate(ctx.LoanedAtUtc)` → `LendingDomain.DueDate(ctx.LoanedAtUtc, ctx.MemberType)`。
- **影響分析からの逸脱として報告**: ECO §2 は `src/Library.Api` 全体(Store.cs/Api.cs/Errors.cs/Ids.cs/Program.cs)を diff ゼロと予測していたが、Program.cs はこの1呼び出し箇所を変更する必要があった。理由: `LendingDomain.DueDate` のシグネチャを区分依存にした結果、既存の呼び出し式がコンパイルできなくなるため(`ctx.MemberType` は `LoanContext` に ECO-001 以来既に存在する。配線自体は新設ではなく、既存フィールドを1関数呼び出しへ追加で渡すだけ)。
  - 代替案として「Library.Core 側に区分非依存の `DueDate` オーバーロードを残し Program.cs を無改修にする」ことも検討したが、それは呼び出し側が誤って旧シグネチャ(standard 固定14日相当)を使い続けるリスクを埋め込むことになり、ECO の主旨(期間の区分依存化を Core の1点に閉じる)に反すると判断し採らなかった。
  - Store.cs / Api.cs / Errors.cs / Ids.cs は本 ECO で無改修(予測どおり diff ゼロ)。

### 3. test/Library.Acceptance/UnitChecks.cs (M-ACCEPTANCE-HARNESS-001)
- `Due(string z)` ヘルパーに `MemberType memberType = MemberType.Standard` の既定引数を追加し、`LendingDomain.DueDate` の新シグネチャに追従(既定 standard なので既存の呼び出し3件は無改修のまま動く)。
- CP-CORE-DUE-001 rev4 の premium vectors 3本を追加(33-control-plan.yaml 記載の3本をそのまま実装):
  - premium 2026-01-31T10:00:00Z → 2026-02-21(月またぎ)
  - premium 2026-12-25T00:00:00Z → 2027-01-15(年またぎ)
  - premium 2026-06-10T23:59:59Z → 2026-07-01(時刻無関係・月末跨ぎ)

## 影響なし予測の検証結果(反証データ)

| 領域 | ECO予測 | 実際 | 一致 |
|---|---|---|---|
| src/Library.Api(Store.cs/Api.cs/Errors.cs/Ids.cs) | diff ゼロ | diff ゼロ | 一致 |
| src/Library.Api/Program.cs | diff ゼロ | **1行の呼び出しシグネチャ変更(diff非ゼロ)** | **不一致(反証。上記§2参照)** |
| E-OVERDUE-BLOCK-001(延滞ブロック) | コード不変 | 無改修(IsMemberOverdue は保存済み due_date のみ参照) | 一致 |
| fineAmount 計算 | 規則・コード不変 | 無改修(Fine は確定済み DateOnly dueDate を引数に取る) | 一致 |
| E-LOAN-LIMIT-001(上限) | 不変 | 無改修 | 一致 |
| K-BOM | 変更なし | 変更なし | 一致 |

Program.cs の1行変更は「振る舞い」ではなく「関数呼び出しの配線」であり、Store.cs 以下のような契約・スキーマ・HTTP応答形には一切触れていない。ECO の予測は「Api層の振る舞いに影響しない」という主張としては成立しているが、「Api層のソースに全く触れない」という字義どおりの予測は成立しなかった。これは新シグネチャを Core 側に導入した設計判断(既定引数によるオーバーロード非採用)に起因する。

## 裁量で決めたこと

- `LoanPeriodDays(MemberType)` 関数名・シグネチャの形は BOM に明示されていない(BOM は「区分依存日数」とだけ記述)。`LoanLimit(MemberType)` という既存の対称的な関数が既にコードベースにあったため、それに倣って同じパターンで実装した。命名以外の裁量の余地はない(値は33-control-plan.yaml/30-ebom.yamlで standard=14/premium=21と明示済み)。
- 定数名を `LoanPeriodDays`(単数固定値)から `LoanPeriodDaysStandard`/`LoanPeriodDaysPremium` の2定数に分割した。BOM は定数名までは指定していないが、`MaxActiveLoansStandard`/`MaxActiveLoansPremium` という既存の命名慣習に揃えた。
- テストの `Due()` ヘルパーに既定引数(`MemberType.Standard`)を与える形にした。BOM はテストヘルパーの実装詳細までは指定していないため、既存の3本(standard 相当)を無改修で動かし続けるための最小の互換設計として選択。

## 影響分析からの逸脱

上記「影響なし予測の検証結果」表の Program.cs の1行を参照。それ以外の逸脱はなし。E-DUE-FINE-001・M-CORE-LENDING-001・M-ACCEPTANCE-HARNESS-001・CP-CORE-DUE-001 は予測どおりの改修範囲に収まった。

## 自己受入の実行結果

- `dotnet build Library.sln -c Release`: **成功・エラー0・警告4件**。警告はすべて `NU1903`(`SQLitePCLRaw.lib.e_sqlite3` 2.1.10 の既知脆弱性アドバイザリ)で、複製時点のベースライン(改修前コミット)でも同数発生することを `git stash` で確認済み。ECO-002 の変更によって新規に生じた警告はゼロ。K-BOM(K-SQLITE-001)は `Microsoft.Data.Sqlite 9.0.0` 固定を指示しており、この推移的パッケージの更新は本 ECO のスコープ外と判断し変更していない。
- `dotnet run --project test/Library.Acceptance`(既定 Debug 構成、work order 記載のコマンドどおり): **37 passed, 0 failed**。
  - unit: 31 件 PASS(CP-CORE-AVAIL-001 ×5 / CP-CORE-LIMIT-001 ×5 / CP-MEMBER-TYPE-001 ×6 / CP-CORE-DUE-001 ×6(standard 3 + premium 新規3)/ CP-CORE-FINE-001 ×4 / CP-CORE-OVERDUE-001 ×4)
  - L1 API スモーク: 6 件 PASS(全6エンドポイント)
  - 合計 31 + 6 = 37、FAIL 0、exit code 0。

補足: 検証中、`-c Release` でビルドした直後に `-c Release` を明示せず `dotnet run --project test/Library.Acceptance -c Release` を実行したところ、スモークテストのサブプロセス起動が `bin/Debug` を探しに行き見つからず FAIL したケースがあった(自己受入ハーネスは `dotnet run --project <api> --no-build` を configuration 指定なしで呼ぶため、Debug 構成の成果物が必要)。これはビルド/実行コマンドの組み合わせの問題であり実装のバグではないことを、Debug 構成でのクリーンビルド+実行で確認済み(上記結果は Debug 構成でのクリーン実行)。Release 構成のビルド自体はエラー0で成功している。
