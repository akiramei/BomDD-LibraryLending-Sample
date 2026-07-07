# sbom-vendor-leak-librarylending-01 — vendor leak 遡及測定(1 例目・scratch)

目的: 交換前の vendor leak プロファイルが実交換コスト(forward-04)を予測できたかの較正。
exchangeability 仮説の 1 例目。対象= BomDD-LibraryLending-Sample、
交換前状態= tag `v0.5-forward-03`、実交換= ECO-005(9fd8367 起票+8a6c2be 製造= tag `v0.6-forward-04`)。
測定日= 2026-07-08。

## 部品同定(測定の前提)

交換された調達部品= **ADO.NET プロバイダ**(Microsoft.Data.Sqlite 10.0.9+SQLitePCLRaw.bundle_e_sqlite3
3.0.3 → System.Data.SQLite.Core)。**SQLite エンジン自体は別の調達品目であり交換されていない**。
固有識別子= パッケージ名 `Microsoft.Data.Sqlite`・名前空間 using・型 prefix `Sqlite[A-Z]*`。

## 測定(v0.5-forward-03 時点の旧部品固有名の出現)

分類対象= As-Maintained 個体(loops/forward-03/part-swap-01)+現行台帳。
歴代工場成果物(loops/forward-01〜02)は as-built アーカイブ= 記録扱いで漏れに数えない。

| 区分 | 出現 | 内訳 |
|---|---|---|
| product-contract leak(10/20/30) | **0** | — |
| oracle leak(41/oracle)| **0** | — |
| K/M-contained | **14** | 31-kbom 3・32-mbom(procurement)3・As-Maintained src 8(csproj 1+using 1+型名 6 — 全て private/内部) |
| procurement/記録(正常・漏れでない) | 9+歴代≈30 | 40-work-order 1(調達指示)・53 reverse_lookup 等 4・60-eco-003 台帳 4・歴代 loops |
| data-format leak | **0** | fixtures/*.db の「SQLite format 3」は**エンジン**のファイル形式= プロバイダ非依存 |
| diagnostic leak | **0** | Sqlite* 型は全て内部(接続管理・Migrate・Reader)。API エラー面・オラクル面に provider 例外名なし |

対照(部品同定の較正): エンジン名 `sqlite` の契約層出現= 10-requirements 3・20-spec 1・30-ebom 8。
これは交換されなかった部品(エンジン= 製品特性)への参照であり、改訂されないのが正しい。

## 事前予測 vs 実績

| 測定項目 | 値 |
|---|---|
| 旧部品固有名の総出現(Microsoft.Data.Sqlite) | 45(うち As-Maintained+現行台帳 23) |
| product-contract leak | 0 |
| oracle leak | 0 |
| K/M-contained | 14 |
| data-format leak | 0 |
| diagnostic leak | 0 |
| **事前予測**(leak プロファイルから) | 交換は 31/32+実装 unit+53/60 改訂+部分再製造で閉じる。10/20/30/41 無改訂 |
| **実績**(ECO-005 固有 diff。混入 3 コミット= bae1bdf/bd5e7d3/131793c を除外) | 起票= 31-kbom(K v3)/32-mbom(procurement)/53(DEG-002)/60-eco-005。製造= 影響 unit の部分再製造+52/53/60 クローズ+V01 の As-Maintained ポインタ更新 |
| REQ/仕様/E-BOM/オラクル改訂の有無 | **no**(41 固定オラクル無改訂・oracle/V01 の変更は watch ポインタ) |
| 実 DB 互換検査の有無 | yes(移行オラクル M01–M04 が兼務・合格) |
| **予測と実交換面の一致** | **match** |

## 仮説の判定(1 例目)

> vendor leak が K/M 層に閉じている場合、部品交換は K-BOM+M-BOM の改訂と部分再製造で閉じる。

**確証(match)**。leak プロファイル(product-contract 0・oracle 0)は forward-04 の正常形
(REQ/仕様/E-BOM/オラクル無改訂)を正しく予測した。

## 副産物の発見 3 件

1. **部品同定の粒度が指標の生死を分ける**: naive な `sqlite` grep は契約層 12 件ヒットで
   「重い交換(製品契約改訂)」と誤予測する。エンジンとプロバイダは別の調達品目 —
   leak 測定の単位は**調達部品の固有識別子**(パッケージ名・名前空間・型 prefix)であり、
   部品ファミリ名ではない。exchangeability 属性を将来設計するなら、各調達部品に
   識別子集合(grep パターン)を宣言させるのが機械化の前提。
2. **data-format の帰属先は部品単位で分かれる**: DB ファイル形式はエンジン帰属であり
   プロバイダ非依存 — forward-04 で実 DB 互換検査が合格した構造的理由が grep で見える。
   data-format leak は「どの部品の形式か」を分けて数える。
3. **watch 検査は As-Maintained ポインタを持つため、再製造は watch の追随更新を生む**
   (V01 のパス更新・微小)。交換コストの第 4 因子(1〜3= vendor leak・再認証自動化率・
   K-BOM 適応知識、に次ぐ微小項)。

## 限界

N=1・小規模(部品交換 1 件・改修 1 unit)・遡及測定(予測は「v0.5 時点で測ったら」の再構成であり
真の事前凍結ではない)・混入 3 コミットの除外は件名ベース。2 例目= ViewPrism2(部品数十・53 実物
あり・ECO-054 の adapter 交換が題材候補)。テンプレ(53)への exchangeability/license/freshness
フィールド追加は 2 例目の後に判定(既知の穴として記帳済み)。
