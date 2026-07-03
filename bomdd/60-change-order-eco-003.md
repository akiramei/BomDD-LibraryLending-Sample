# Change Order — ECO-003(forward-03: 調達部品交換 — Service BOM 複合イベント)

> Phase 7 の第3回。狙いは **Service BOM(外部知識劣化)と ECO の複合イベント**の初回検証 —
> 変更の起点が要求(ECO-001/002)でなく**外部世界**(調達部品の脆弱性アドバイザリ)であるケースを、
> Service BOM の逆引き → 処置裁定 → 調達交換 → 再認証、の連鎖で通す。
> 実施形態: **設計者適用+全再認証**(コード製造なし=csproj 2行の調達交換。fresh 工場は使わない —
> 処置語彙「部品交換+全再認証」の初データ。akira 裁定 2026-07-04)。

## 0. 劣化イベント(変更の起点 — 53 DEG-001)
- 検出: forward-02 の**両工場が NU1903 を申告**(cheat-report)→ 設計者が
  `dotnet list package --vulnerable --include-transitive` で確定。
- 内容: 調達部品 Microsoft.Data.Sqlite 9.0.0 の推移的依存 **SQLitePCLRaw.lib.e_sqlite3 2.1.10** に
  **High** 深刻度 **GHSA-2m69-gcr7-jv3q**(同梱 SQLite の脆弱性)。
- Service BOM 逆引き(53): affected = **SB-SQLITE-STORE-001 のみ**(他 SB item は非依存=再検査不要)。
- baseline: tag `v0.4-forward-02`(As-Maintained = factory-eco2-01-opus)。

## 1. 処置の裁定(と再裁定 — 実装検証が裁定を確定する)
- 当初裁定(akira): **α = Microsoft.Data.Sqlite 10.0.9 へ更新**(32-mbom 調達注記「10.x への更新は
  ECO で扱う」の履行+最新 stable+net10.0 TFM 整合)。
- **前提の反証(scratch 事前検証)**: アドバイザリ範囲は `<= 2.1.11` **全て**で 2.1 系に修正版なし
  (修正は 3.x 系のみ)。10.0.9 は SQLitePCLRaw 2.1.11 を参照するため **α 単独では解消しない**。
- 再裁定(akira 2026-07-04): **α+γ** = 10.0.9 + **SQLitePCLRaw.bundle_e_sqlite3 3.0.3 の直接参照**で
  推移的依存を上書き。scratch 検証: ビルド 0 エラー・脆弱性クリーン・受入 37/37(実行時互換 OK)。
  — plm ECO-003 の「裁定の再裁定」規律の調達版(処置は仕様でなく外部実物との突合で確定する)。
- 退役条件(調達 note に明記): Microsoft.Data.Sqlite が SQLitePCLRaw 3.x 系を自身の依存として
  参照した時点で、bundle の直接参照は外す(推移的解決へ戻す)。

## 2. 影響分析(製造前に凍結)
### 影響あり
| 段 | 影響 | 何が変わるか |
|---|---|---|
| M-BOM | 32-mbom procurement(9.0.0→10.0.9+bundle 3.0.3 追加宣言・退役条件) | 調達のみ |
| K-BOM | K-SQLITE-001 → **v2**(version 行+「9.0.0 のみ」宣言の改訂) | |
| Service BOM | 53 新設(SB 3 items+DEG-001)・reinspection_result 記入 | 本 ECO で層自体を新設 |
| 実装 | **src/Library.Api/Library.Api.csproj の 2 行のみ**(Version 属性+PackageReference 1 行追加) | |
| オラクル | **V01 新設**(oracle/vulnerability-oracle.ps1 = 脆弱パッケージ 0 件)。既存 S/E/M 行は全て不変 | |

### 影響なし予測(反証可能 — 適用前に凍結)
| 領域 | 予測 | 根拠 |
|---|---|---|
| **全 .cs ファイル** | **diff ゼロ** | 調達交換のみ。API 表面(Microsoft.Data.Sqlite の使用箇所)は 9.x→10.x で互換(scratch でビルド 0 エラー+受入 37/37 を事前確認) |
| S01–S28(挙動全域) | 全 PASS 維持 | SQLite の観測可能な挙動契約(SQL 方言・接続文字列・user_version)は不変 |
| E01–E04(効力) | 全 PASS 維持 | データ規則に触れない |
| M01–M04(v0.2 移行) | 全 PASS 維持 | 移行コード不変。**ただしここが最リスク**(SQLite 同梱バージョン差による PRAGMA/型挙動の変化があるなら移行と永続化に出る — 全再認証を課す理由) |
| BOM の他層(10/20/30/33/34/41 既存行) | 改訂なし | 要求・仕様・E-BOM の挙動契約に変更がない(調達は M-BOM の管轄) |

### 結果分類(適用前に固定)
| 観測 | 分類 |
|---|---|
| S/E/M 既存行の失敗 | **regression**(部品交換起因) |
| V01 の失敗 | **change miss**(処置が劣化を解消していない) |
| .cs への diff | **unnecessary modification**(=影響なし予測の反証) |

### 成功条件
交換後個体が **S01–S28・E01–E04・M01–M04 全 PASS + V01 PASS**(.cs diff ゼロ)。

## 3. 較正(negative control)
- **V01 を変更前個体(v0.4)へ実行し FAIL(GHSA-2m69-gcr7-jv3q 検出)を確認**してから凍結
  (V01 の弁別力の証明。既存オラクルは v0.4 で全 PASS 済み=forward-02 受入)。
- 凍結 tag: `forward-03-input`

## 4. 適用と再認証(設計者)
- v0.4 個体(factory-eco2-01-opus)の追跡ソースを `loops/forward-03/part-swap-01/` へ複製・コミット
  (diff 基準点)→ csproj 2 行の交換を適用 → 全再認証(fixed 28 + effectivity 4 + migration 4 + V01)。
- 採用: 再認証全緑なら本個体を As-Maintained **v0.5** とする(tag `v0.5-forward-03`)。

## 5. 記録(2026-07-04 実施)
| 測定 | part-swap-01(設計者適用・採用= v0.5) |
|---|---|
| 変更受入 V01(脆弱パッケージ 0 件) | **1/1 PASS**(較正では v0.4 に対し FAIL=2.1.10 検出 2 件) |
| 回帰 S01–S28 | 28/28 = **regression 0** |
| 効力 E01–E04 | 4/4 |
| 移行回帰 M01–M04(最リスク領域=新 SQLite 3.x ネイティブでの v0.2 移行) | 4/4 |
| 影響なし予測(全 .cs diff ゼロ) | **的中**(diff = Library.Api.csproj 2 行のみ) |

- **複合イベントの連鎖が成立**: 外部劣化(High アドバイザリ)→ Service BOM 逆引き(affected 1/3 SB)→
  処置裁定(+再裁定)→ 調達交換(csproj 2 行)→ **全再認証**、が既存のオラクル資産(S/E/M)+
  新設 V01 だけで閉じた。fresh 工場なし=処置語彙「**部品交換+全再認証**」の初データ。
- **裁定の再裁定の調達版**: 当初処方 α は scratch 事前検証で不成立と判明(アドバイザリ範囲 <= 2.1.11)。
  処置は外部実物(アドバイザリ本文・依存解決の実測)との突合で確定する — 部品交換の影響分析は
  BOM でなく**パッケージグラフの実測**(dotnet list --vulnerable / 依存解決)を一次資料にする。
- 劣化検出の出所: **製造工場の正直な申告(NU1903)が Service BOM の検出器として機能した**
  (forward-02 §4 の副産物申告 → DEG-001)。ずる報告チャネルは劣化イベントの受信機を兼ねる。
- 採用: part-swap-01 を As-Maintained **v0.5** とする(tag `v0.5-forward-03`)。
