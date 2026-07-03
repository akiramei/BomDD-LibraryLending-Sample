# Change Order — ECO-002(forward-02: 会員区分依存の貸出期間)

> Phase 7 の第2回検証。狙いは **影響グラフが非自明な ECO での影響分析の弁別力の測定**(forward-01.5 §4 の限界
> 「影響分析が自明に近い規模」への応答)。変更は依存グラフの中心(期限規則)に入るが、**正しい分析は
> 「依存があるのに影響しない」を切り出せる**はず — 延滞判定・料金・API 層は確定済み dueDateUtc(保存値)を
> 経由して結合しており、コード変更は Core の期限計算1点に閉じると予測する。
> 規律: 既存固定オラクル S01–S25 と移行オラクル M01–M04 は**不変**(回帰のヤードスティック)。追加分は S26–S28 + 効力 E01–E04。

## 0. 変更前 baseline の凍結
- 成果物 baseline: tag `v0.3-forward-015`。**As-Maintained 個体 = factory-eco-01-opus のビルド**(akira 裁定 2026-07-04:
  v0.2 系譜 factory-04-opus と連続)。本 ECO はこの個体の改修として実施。
- 固定オラクル: S01–S25(凍結済み・不変)+ 移行 M01–M04(不変。ECO-002 はスキーマ変更なしだが、v0.2→rev4 の
  直接移行が壊れていないことの回帰として再走行する)
- DB fixture(新設): `oracle/fixtures/baseline-v03.db` + `baseline-v03-manifest.json`(As-Maintained v0.3 ビルドで作成。
  standard/premium 各会員の active 貸出[旧規則 +14日]・returned 貸出を含む。効力オラクルの入力)

## 1. 変更要求
- ECO-ID: ECO-002
- 種別: REQ 改訂 1 件(REQ-003 rev2)
- 変更内容(PO 代行裁定。根拠精度 G1 適用):
  - **REQ-003(改訂 rev2)**: 貸出期限 = 貸出日(UTC 暦日)+ **区分依存日数** — `standard` = **14日** / `premium` = **21日**。
  - **効力(rev2 に含む)**: 期限は**貸出作成時に確定**し、以後の規則・区分変更に追従しない。既存貸出
    (本改訂前に作成された premium 会員の貸出を含む)の dueDateUtc は不変。延滞判定・延滞料金は常に
    その貸出の確定済み dueDateUtc を基準にする。
  - スキーマ変更なし(memberType は ECO-001 で導入済み・dueDateUtc は貸出行に保存済み)= **移行イベントなし**。

## 2. 影響分析(トレース逆引き — 製造前に凍結)

### 影響あり
| 段 | 影響 ID | 何が変わるか |
|---|---|---|
| 仕様節 | §2.4(dueDateUtc 定義=区分依存+効力)/ §1 rev 注記 | rev4 |
| REQ | REQ-003 rev2 | |
| E-BOM | **E-DUE-FINE-001 のみ**(invariants 改+depends_on に E-MEMBER-TYPE-001 追加) | 12品目中1 |
| M-BOM | **M-CORE-LENDING-001 のみ**(期限計算)+ M-ACCEPTANCE-HARNESS-001(vectors 追加) | **ECO-001(3/3)と対照の絞り込み予測: コード unit 1/2** |
| Control Plan | CP-CORE-DUE-001(vectors を区分対応に) | |
| FMEA | **FMEA-006(新)**: 期間日数の再計算による遡及(効力違反)| |
| 固定オラクル | **追加のみ**: S26(premium 期限)/ S27(料金の期限追従)/ S28(ブロックの期限追従)。S01–S25 不変 | |
| 専用オラクル | **effectivity-oracle(新設)**: E01–E04 + fixture baseline-v03(§5) | |
| K-BOM | 変更なし(移行規律の追加なし=スキーマ不変) | |

### 影響なし予測(反証可能な予測 — 本 ECO の主測定値)
> 前回(ECO-001)は 3/3 unit が影響し絞り込みが測れなかった。今回は**依存グラフの中心に触れながら**
> 以下を「影響なし」と予測する。外れ=影響分析の under/over-inclusion として計上。

| 領域 | 予測 | 根拠(グラフを辿った上で「影響しない」と切る理由) |
|---|---|---|
| **src/Library.Api(Store.cs / Api.cs / Errors.cs / Ids.cs / Program.cs)** | **ソース diff ゼロ** | dueDateText は Core の判定関数が返し、Store/Api は透過に保存・応答する。LoanContext は ECO-001 以降 MemberType を運んでいる=配線変更も不要 |
| E-OVERDUE-BLOCK-001(延滞ブロック) | コード不変 | depends_on: E-DUE-FINE-001 **だが**、判定は保存済み due_date_utc の読み出し比較(INV-3)。期間規則を参照しない。premium の新期限には**データ経由で自動追従**(S28 が検証) |
| fineAmount 計算 | 規則・コード不変 | 料金は確定済み dueDateUtc からの暦日差。期間日数に依存しない(S27 が自動追従を検証) |
| E-LOAN-LIMIT-001(上限) | 不変 | 上限(3/5)と期間(14/21)は独立の区分依存規則 |
| S01–S25 全ケース | 全 PASS 維持 | standard の期限・料金・ブロックは全て不変。**S25(premium 上限5)は dueDateUtc を検査していない**(凍結前に治具実装を確認済み)ため premium 期間変更の影響を受けない |
| M01–M04(v0.2 移行) | 全 PASS 維持 | スキーマ不変。移行後の既存会員は standard=期限規則も従来どおり |
| エラー語彙・応答形 | 不変 | 新エラーコードなし・新フィールドなし |
| E-SQLITE-STORE / E-PERSISTENCE / E-HTTP-CONTRACT / E-DATETIME / E-ID | 不変 | スキーマ・契約・受理形に触れない |

## 3. BOM 改訂(bom_rev: rev3 → rev4)
- 改訂ファイル: 10-requirements(REQ-003 rev2)/ 20-spec(§2.4 rev4)/ 30-ebom(E-DUE-FINE-001)/
  32-mbom(FMEA-006)/ 33-control-plan(CP-CORE-DUE-001 vectors)/ 41-fixed-oracle(S26–S28+effectivity_oracle)/
  oracle/fixed-oracle.ps1(S26–S28)/ oracle/effectivity-oracle.ps1(新)/ oracle/fixtures/baseline-v03.*(新)
- **変更分の受入を製造より先に追加**(オラクル・ファースト)。治具はセルフテスト+**較正(negative control)**:
  変更前個体(v0.3 = factory-eco-01-opus)に対して fixed-oracle v4 = **S01–S25 PASS・S26–S28 のみ FAIL**、
  effectivity-oracle = **E01/E02/E04 PASS・E03 のみ FAIL** を確認してから凍結。
- **較正の実施記録(2026-07-04)**: 治具セルフテスト PASS / fixed-oracle v4 vs v0.3 = **25/28(S01–S25 全PASS・
  S26–S28 のみ FAIL。FAIL 実測値は旧規則どおり: premium due=+14日・fine=700/800・+15日でブロック)** /
  effectivity-oracle vs v0.3 = **3/4(E01/E02/E04 PASS・E03 のみ FAIL、実測 due=2026-05-24=+14日)** =
  想定プロファイルに一致。fixture 実測: premA1 due=2026-05-15 / premA2 due=2026-05-16(旧規則の確定値)。
  記録: loops/forward-02/calibration-fixed-v03.json / calibration-effectivity-v03.json
- 凍結 tag: `forward-02-input`

## 4. 部分再製造の計画(隔離条件 — ECO-001 の承認済み条件を踏襲)
- **工場数: 2(opus / sonnet、fresh)**。
- 工場に渡すもの: **v0.3 個体のソース複製(factory-eco-01-opus 由来。各工場専用ディレクトリに設計者が事前複製・
  コミット=diff 基準点)+ 本 ECO(§0–§5。影響なし予測含む)+ 改訂済み BOM(10/20/30–34/40)**。
- **渡さないもの**: 設計対話 / S26–S28 の治具実装 / effectivity-oracle の実装 / **baseline-v03 fixture と manifest** /
  探索プローブ / 他工場成果 / 旧 cheat-report / 較正記録。
- 指示: 「ECO の影響分析にある箇所**だけ**を改修せよ。影響なし箇所への変更は禁止(特に src/Library.Api は
  diff ゼロを予測している — 変更が必要と判断したら、それ自体を報告せよ=影響分析の反証データ)」。
- **不要改変の測定**: 納品後に複製時点との git diff。分類は ECO-001 と同一
  (format/noise / test-only / behavior-risk / contract-change)。
- 自己受入: 既存ハーネス(unit+L1 スモーク)+ CP-CORE-DUE-001 rev4 vectors(premium 3本)。**赤=stop/report**。

### 結果分類(製造前に固定)
| 観測 | 分類 |
|---|---|
| S01–S25 / M01–M04 の失敗 | **regression** |
| S26–S28 / E03 の失敗 | **change miss** |
| E01/E02/E04 の失敗 | **data-preservation miss(効力違反)** |
| 影響分析外への diff(特に src/Library.Api) | **unnecessary modification** — Api 層の diff は同時に**影響なし予測の反証**として under-inclusion にも計上 |
| 自己受入赤での停止 | **manufacturing nonconformance**(採点対象外) |

### 成功条件
2 fresh 工場が **S01–S28・M01–M04・E01–E04 を通過**し、不要改変が format/noise・test-only に収まること。
**最良級の失敗データ**: (a) Api 層に diff が出る=「diff ゼロ」予測の反証 (b) E02/E04 が落ちる=効力
(遡及なし)が BOM に書けていない証拠 (c) S27/S28 が落ちる=「データ経由の自動追従」の見立て違い。

## 5. 効力(effectivity)専用オラクル(E01–E04)
fixture(v0.3 個体で作成した実 DB。premium 会員の active 貸出=旧規則 +14日を含む)のコピーに対して rev4 ビルドを起動して検査:
| ID | 検査 | 失敗時の分類 |
|---|---|---|
| E01 | v0.3 の DB で rev4 が起動し、既存の蔵書・会員・貸出一覧が manifest と同値 | data-preservation miss |
| E02 | 既存 premium 会員の**既存** active 貸出の dueDateUtc = manifest 値(+14日のまま。遡及なし) | data-preservation miss(効力違反) |
| E03 | 同じ既存 premium 会員の**新規**貸出は dueDateUtc = +21日(新規則は新規のみ) | change miss |
| E04 | 既存 premium 会員の既存貸出を旧期限+1日で返却 → fineAmount=100(確定済み dueDateUtc 基準。FMEA-006) | data-preservation miss(効力違反) |

## 6. 記録(製造後に記入)
