# Change Order — ECO-005(forward-04: 互換性が壊れる部品交換 — 再製造への昇格)

> Phase 7 の第4回。狙いは Service BOM 処置語彙の**未観測の第4項「再製造必要」の初適用** —
> 部品交換が互換性の壁で不成立になったとき、処置が「交換+全再認証」から**再製造**へ昇格する連鎖
> (検出 → 逆引き → 交換候補 → **scratch 赤** → 昇格裁定 → K/M 層改訂 → fresh 工場の部分再製造 → 全再認証)を通す。
> イベントは**シナリオ起票**(53 DEG-002 に宣言済み — 組織裁定であり外部事実の捏造なし)。
> **非互換性そのものは実在の部品差**(演出しない): 交換のみの scratch ビルドが実測で赤になる。

## 0. 劣化イベント(変更の起点 — 53 DEG-002)

- 内容: 調達方針裁定(akira 委任裁定 2026-07-04)= **SQLitePCLRaw 系列からの供給網離脱**。
  DEG-001(GHSA-2m69-gcr7-jv3q・実 High アドバイザリ)の再発文脈+32-mbom の bundle 直接参照が
  退役条件付き暫定調達であることを受け、SQLitePCLRaw 非依存の代替部品への交換を指示。
- 代替部品: **System.Data.SQLite.Core 1.0.119**(sqlite.org 系 ADO.NET プロバイダ・SQLitePCLRaw 非依存)。
  事前確認: `dotnet list package --vulnerable --include-transitive` = **脆弱 0 件**(V01 成立の前提)。
- Service BOM 逆引き(53): affected = **SB-SQLITE-STORE-001 のみ**(DEG-001 と同一部品系列)。
- baseline: tag `v0.5-forward-03`(As-Maintained = loops/forward-03/part-swap-01)。

## 1. 処置の裁定 — 語彙の梯子と昇格(本 ECO の主測定)

処置語彙の梯子を下から順に scratch 事前検証(forward-03 で工程化)にかけた:

| 段 | 候補 | scratch 検証結果 | 裁定 |
|---|---|---|---|
| 1 | K-BOM更新のみ(再認証) | 該当せず(部品自体が変わる) | — |
| 2 | **部品交換+全再認証**(csproj のみ・コード不変) | **build 不成立: 6 エラー**(CS0234 ×1+CS0246 ×5・全て Store.cs — 名前空間 `Microsoft.Data.Sqlite`→`System.Data.SQLite`・型名 `SqliteConnection`→`SQLiteConnection` 等の実在 API 差) | **不成立** |
| 3 | **再製造必要** | → 本 ECO で実施 | **採択**(語彙第4項の初適用。akira 委任裁定 2026-07-04) |

**主発見(予測どおりか検証)**: 昇格の決定点は「適用後の全再認証の赤」ではなく
**scratch 事前検証に前倒しされた**。決定的な非互換(コンパイル/挙動差)は scratch が先取りするため、
「適用後に回帰が赤くなる」のは scratch で再現できない非互換(環境依存・非決定的・実データ依存)に限られる
— method 還元候補(s-bom-template 手順3の昇格パスの実像)。

## 2. 影響分析(製造前に凍結)

### 影響あり
| 段 | 影響 | 何が変わるか |
|---|---|---|
| M-BOM | 32-mbom procurement: Microsoft.Data.Sqlite 10.0.9+bundle_e_sqlite3 3.0.3 → **System.Data.SQLite.Core 1.0.119**(SQLitePCLRaw 退役=退役条件も同時解消) | 調達の交換 |
| K-BOM | **K-SQLITE-001 → v3**(新部品の慣行知識: 名前空間/型名・接続文字列・パラメータ・PRAGMA 可用性) | |
| Service BOM | 53: DEG-002 追記(起票済み)・SB-SQLITE-STORE-001 external_deps v3+reinspection_result(受入後) | |
| 実装 | **unit M-API-SQLITE-001 のみ**: `src/Library.Api/Store.cs`(適応)+`Library.Api.csproj`(調達交換) | 再製造対象 |
| オラクル | **改訂なし**(S01–S28/E01–E04/M01–M04/V01 全行不変 — 挙動契約は不変・V01 は部品非依存の汎用検査) | |

実物点検(61 §1.2): 旧部品 API の使用は `Store.cs` の 6 サイトのみ(`grep Microsoft.Data.Sqlite|Sqlite*` —
Library.Core・test/ は非依存)。日時は全て TEXT を自前整形(`GetDateTime` 不使用)= プロバイダの
日時型変換に非依存。**unit 粒度の絞り込み: 影響 1/3 unit**(M-API-SQLITE-001 のみ。局所性が高い変更)。

### 影響なし予測(反証可能 — 製造前に凍結)
| 領域 | 予測 | 根拠 |
|---|---|---|
| src/Library.Core 全ファイル | **diff ゼロ** | ドメイン判定は DB 非依存(部品交換は M-API-SQLITE-001 に閉じる) |
| test/ 全ファイル | **diff ゼロ** | 受入ハーネスは HTTP/プロセス境界でのみ接触 |
| src/Library.Api の Store.cs 以外(Program.cs 等) | **diff ゼロ** | 旧部品 API の使用サイトは Store.cs のみ(実物点検) |
| S01–S28(挙動全域) | 全 PASS | 挙動契約(HTTP/JSON・日時・ID・ドメイン規則)はプロバイダ非依存 |
| E01–E04(効力) | 全 PASS | データ規則に触れない |
| M01–M04(v0.2 移行) | 全 PASS(**最リスク**) | 既存 db ファイル= 旧部品が書いた実データを新部品で開き移行する = **「永続化(データ互換性)」沈黙次元(32-mbom out-of-scope 宣言)の初の実検査**。低リスク根拠: ファイル形式は同一 SQLite3・日時は TEXT 自前整形・user_version PRAGMA は SQLite 本体機能 |
| V01(脆弱 0 件) | PASS | 事前確認済み(§0) |

### 結果分類(製造前に固定)
| 観測 | 分類 |
|---|---|
| S01–S28 / E01–E04 / M01–M04 の失敗 | **regression**(再製造起因) |
| V01 の失敗 | **change miss**(処置が供給網離脱を達成していない) |
| Store.cs+csproj 以外への diff | **unnecessary modification**(=影響なし予測の反証・under-inclusion 計上) |
| 自己受入赤での停止 | manufacturing nonconformance(採点対象外) |

### 成功条件
再製造個体が **S01–S28・E01–E04・M01–M04 全 PASS + V01 PASS**、かつ diff が
`src/Library.Api/Store.cs`+`Library.Api.csproj` に閉じること(+SQLitePCLRaw 参照の完全消滅)。

## 3. 較正(negative control)

- 本 ECO の弁別力の証明は **scratch 交換のみビルド= 赤 6 エラー**(§1 表・2026-07-04 実測)。
  オラクル行の改訂がない(変更断面= 非互換の解消そのもの)ため、S-22 型の期待値較正は該当なし —
  「交換のみでは成立しない」ことの実測が negative control に相当する。
- 凍結 tag: `forward-04-input`(改訂 BOM+工場入力複製のコミット)。

## 4. 部分再製造の計画(隔離条件 — ECO-001/002 の承認済み条件を踏襲)

- **工場数: 1(sonnet・fresh)**。1 体の理由: 本 loop の測定対象は**昇格機構の連鎖**であり、
  工場分散は forward-01(5工場)/forward-02(2工場)で測定済み。適応は裁量次元の小さい
  局所変更(1 ファイル)で、分散測定の期待情報量が小さい(akira 委任裁定)。
- 工場に渡すもの: **v0.5 個体のソース複製**(loops/forward-04/factory-eco5-01-sonnet/ に設計者が
  事前複製・コミット= diff 基準点)+**本 ECO(§0–§4。影響なし予測含む)**+改訂済み BOM
  (20/30/31 v3/32 v3/33/34)。
- 渡さないもの: 設計対話 / オラクル治具実装(fixed/effectivity/migration/vulnerability)/
  baseline fixture / 他 loop 成果 / 旧 cheat-report / scratch 検証の詳細(エラー一覧は §1 表の要約のみ)。
- 指示: 「ECO の影響分析にある箇所**だけ**を改修せよ(Store.cs+csproj)。影響なし箇所への変更は禁止
  (Library.Core / test/ / Program.cs は diff ゼロを予測している — 変更が必要と判断したら、それ自体を
  報告せよ= 影響分析の反証データ)。自己受入(unit+L1 スモーク)を全緑にしてから納品。赤= stop/report」。
- 不要改変の測定: 納品後に複製時点との git diff(63)。分類は ECO-001 と同一。

## 5. 記録(実施後に記入)
