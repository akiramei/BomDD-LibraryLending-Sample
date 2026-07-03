# forward-03 報告 — Service BOM 複合イベント(ECO-003: 調達部品交換)

> **一文要旨**: 変更の起点が要求でなく**外部世界**(調達部品の High 脆弱性アドバイザリ)であるケースを、
> **Service BOM 逆引き → 処置裁定(+再裁定)→ 調達交換(csproj 2 行)→ 全再認証**の連鎖で通し、
> **V01 1/1・回帰 28/28・効力 4/4・移行 4/4・.cs diff ゼロ**で閉じた。fresh 工場なし —
> 処置語彙「**部品交換+全再認証**」の初データ。

- 入力凍結: tag `forward-03-input` / 結果: tag `v0.5-forward-03`(As-Maintained = part-swap-01)
- ECO 本体: [bomdd/60-change-order-eco-003.md](../../bomdd/60-change-order-eco-003.md)(§5 に測定記録)
- 裁定(2026-07-04): 処置 = α+γ(Microsoft.Data.Sqlite 10.0.9 + SQLitePCLRaw.bundle_e_sqlite3 3.0.3
  直接参照・退役条件付き)/ 実施形態 = 設計者適用+全再認証

## 1. イベントの経緯(複合イベントの連鎖)
1. **検出**: forward-02 の製造工場 2 体が NU1903 を cheat-report で正直申告(裁量・環境ノイズの申告チャネル)
   → 設計者が `dotnet list package --vulnerable --include-transitive` で確定 →
   **GHSA-2m69-gcr7-jv3q(High)**: SQLitePCLRaw.lib.e_sqlite3 2.1.10(調達部品 Microsoft.Data.Sqlite 9.0.0 の推移的依存)
2. **Service BOM(53 新設)逆引き**: affected = SB-SQLITE-STORE-001 のみ(3 SB 中 1。他は非依存=再検査不要)
3. **処置裁定と再裁定**: 当初 α(10.0.9 単独)は scratch 事前検証で**不成立と判明**
   (アドバイザリ範囲 `<= 2.1.11` 全て・2.1 系に修正版なし=10.0.9 も脆弱な 2.1.11 を参照)→
   akira 再裁定で **α+γ**(bundle 3.0.3 直接参照で推移的依存を上書き)
4. **凍結**: V01(脆弱性オラクル)新設+較正(v0.4 に対し FAIL=弁別力確認)→ tag
5. **適用と再認証**: 複製コミット(diff 基準点)→ csproj 2 行交換 → 4 オラクル全緑

## 2. 結果
| 測定 | part-swap-01(採用= v0.5) |
|---|---|
| 変更受入 V01(脆弱 0 件) | 1/1(較正: v0.4 で FAIL) |
| 回帰 S01–S28 | 28/28 |
| 効力 E01–E04 | 4/4 |
| 移行回帰 M01–M04(最リスク=新 SQLite 3.x ネイティブでの v0.2 移行) | 4/4 |
| 影響なし予測(全 .cs diff ゼロ) | **的中**(diff = csproj 2 行のみ) |

## 3. 知見(method 還元候補)
1. **ずる報告チャネルは劣化イベントの受信機を兼ねる**: 工場の正直な環境ノイズ申告(NU1903)が
   Service BOM の change_signal になった。ずる台帳と Service BOM の接続は設計されていなかったが機能した
   — 工場申告→DEG 起票の経路を工程として明文化する価値。
2. **調達処置の影響分析は「パッケージグラフの実測」が一次資料**: BOM のトレース逆引きでは
   推移的依存の脆弱性範囲を辿れない。処置裁定はアドバイザリ本文(影響範囲・修正版)と
   依存解決の実測(scratch 交換検証)で確定する — 裁定の再裁定の調達版
   (当初 α が実測で反証された。forward-02 の「実物点検」の調達グラフ版)。
3. **処置語彙「部品交換+全再認証」が成立**: コード製造が無い ECO は fresh 工場でなく
   設計者適用+既存オラクル資産の全走行で閉じる。既存の S/E/M オラクルが
   「交換部品の互換性検査器」として再利用された(新規に書いたのは V01 のみ)。
4. **退役条件付き調達**: 推移的依存の上書き(γ)は恒久でなく、上流(Microsoft.Data.Sqlite)が
   3.x を参照した時点で外す条件を procurement に明記 — Service BOM の次の watch 項目になる。

## 4. 限界
- 交換部品の互換性が高かった(挙動契約に差が出ないケース)。互換性が壊れる部品交換
  (回帰が赤になり再製造へ昇格するケース)は未観測。
- Service BOM の watch は手動(dotnet list の随時実行)。CI 常設(NU1903 を warning→検査化)は次段候補。
  → **常設済み(2026-07-04)**: .github/workflows/vulnerability-watch.yml(V01 を As-Maintained へ毎日+push 時に実行。
  赤 run = DEG 検出通知)。V01 は FAIL で非ゼロ終了するよう改修(53 の watch 欄参照)。

---
**method 還元済み(2026-07-04・BomDD 92ef701)**: §3 の 4 件 — ずる報告=劣化受信機(s-bom-template 手順1+templates/53)/
調達処置はパッケージグラフ実測が一次資料(同 手順3+playbook §8)/ 処置語彙「部品交換+全再認証」(同 語彙+playbook §8)/
retirement_condition(templates/32 procurement)。総括 = FINDINGS §7.7 / WHITEPAPER §12。
