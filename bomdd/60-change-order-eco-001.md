# Change Order — ECO-001(forward-01.5: 会員区分の導入)

> Phase 7(変更オーダー)の初回検証。狙いは新規製造ではなく **ECO が BOM にどう伝播するか**の測定。
> 規律: 既存固定オラクル S01–S23 は**不変**(回帰のヤードスティック)。追加分は S24+。マイグレーションは専用オラクル。
> 失敗の分類: 既存が壊れた=**regression** / 追加が落ちた=**change miss** / マイグレーションが落ちた=**data-preservation miss**。

## 0. 変更前 baseline の凍結
- 成果物 baseline: tag `v0.2-forward-01-rev2`。**As-Maintained 個体 = factory-04-opus-rev2 のビルド**(本 ECO はこの個体の改修として実施。内部スキーマはこの個体のものが移行入力になる)
- 固定オラクル: S01–S23(凍結済み・不変)
- DB fixture: `oracle/fixtures/baseline-v02.db` + `baseline-v02-manifest.json`(factory-04 v0.2 ビルドで作成した実データ。マイグレーションオラクルの入力)

## 1. 変更要求
- ECO-ID: ECO-001
- 種別: REQ 改訂 1 件 + REQ 追加 2 件
- 変更内容(PO 代行裁定。根拠精度 G1 適用):
  - **REQ-002(改訂)**: 会員の同時貸出上限は**会員区分(memberType)に依存**する — `standard` = 3冊 / `premium` = 5冊。境界は従来同様「上限冊目は成功、上限+1冊目で拒否」。
  - **REQ-008(新規)**: 会員登録時に memberType を任意指定できる(`standard` | `premium`)。未指定の既定は `standard`。それ以外の値は 400 `invalid_request`。会員登録応答に memberType を含める。区分の変更手段は本 ECO のスコープ外(登録時のみ)。
  - **REQ-009(新規)**: **既存の v0.2 データベースを自動移行する**。rev3 のアプリは v0.2 スキーマの DB ファイルで起動でき、既存の蔵書・会員・貸出・返却状態・延滞料金をすべて保持する。既存会員の区分は `standard` とする。既存の active 貸出はそのまま有効で、新ルール下でも上限カウントに数えられる。移行は不可逆でよい(ロールバック不要)が、移行前ファイルの破壊以外の方法であること(=起動失敗やデータ消失をしない)。

## 2. 影響分析(トレース逆引き — 製造前に凍結)

### 影響あり
| 段 | 影響 ID | 何が変わるか |
|---|---|---|
| 仕様節 | §2.3(memberType)/ §2.4-4(区分依存上限)/ §2.7(スキーマ移行)/ §1 スコープ外リスト | rev3 |
| E-BOM | E-LOAN-LIMIT-001(invariants 改)/ **E-MEMBER-TYPE-001(新)**/ E-SQLITE-STORE-001(スキーマ変更)/ **E-MIGRATION-001(新)** | |
| M-BOM | M-CORE-LENDING-001(上限規則)/ M-API-SQLITE-001(members endpoint・スキーマ・移行)/ M-ACCEPTANCE-HARNESS-001(vectors 追加) | **粒度の観察**: unit 粒度では 3/3 が影響=絞り込み効果なし。E-BOM 粒度+不要改変 diff 測定で代替(§4) |
| Control Plan | CP-CORE-LIMIT-001(vectors を区分対応に)/ **CP-MEMBER-TYPE-001(新)**/ **CP-MIGRATION-001(新)** | |
| 固定オラクル | **追加のみ**: S24(memberType 登録・既定・不正値)/ S25(premium 上限5)。S01–S23 不変 | |
| 専用オラクル | **migration-oracle(新設)**: M01–M04(§5) | |
| K-BOM | K-SQLITE-001 → v2(スキーマ版管理+移行規律を追記) | |

### 影響なし予測(反証可能な予測 — Phase 7 の測定値)
> rev3 製造後の回帰(S01–S23)で当たるかを検証する。外れた箇所=影響分析の取りこぼし(under-inclusion)として計上。

| 領域 | 予測の根拠 |
|---|---|
| S01–S06(蔵書・在庫・検証・404) | 在庫判定は区分と独立 |
| **S07(上限4冊目拒否)** | 区分未指定の既定が standard=3 のため**既存挙動と同一**(既定値設計が既存オラクルを保存する) |
| S08–S10, S22(日時ポリシー・出力形式) | 日時系は変更対象外 |
| S11–S14(返却・延滞料金・再返却) | 料金・状態機械は区分と独立 |
| S15–S16(延滞ブロック) | 延滞判定は区分と独立(上限とは別の判定段) |
| S17–S18, S21, S23(過去判定・一覧・ID・空文字) | 変更対象外 |
| S19(再起動永続性) | 新スキーマ内での永続性は同型 |
| S20(NFR) | 区分参照は O(1) 追加 |
| エラー語彙 | 新エラーコードは追加しない(不正区分は既存 invalid_request) |

## 3. BOM 改訂(bom_rev: rev2 → rev3)
- 改訂ファイル: 10-requirements / 20-spec / 30-ebom / 31-kbom / 32-mbom / 33-control-plan / 41-fixed-oracle(S24–S25 追加)/ oracle/migration-oracle.ps1(新)
- **変更分の受入を製造より先に追加**(オラクル・ファースト): S24–S25 + M01–M04 + CP vectors。治具はセルフテスト+**較正(negative control)**: migration-oracle を v0.2 個体に対して実行し、data-preservation 系(M01/M02/M04)が PASS・change 系(S24/S25 相当)が FAIL になることを確認してから凍結する。
- **較正の実施記録(2026-06-10)**: 治具セルフテスト PASS / migration-oracle vs v0.2 = **4/4** / fixed-oracle v3 vs v0.2 = **23/25(S01–S23 全PASS・S24/S25 のみ FAIL)** = 想定プロファイルに一致。較正の初回実行が M03 のシナリオ日付バグ(延滞ブロックとの干渉)を凍結前に捕捉(CHEAT-F015-H001)。
- 凍結 tag: `forward-015-input`

## 4. 部分再製造の計画(隔離条件 — ユーザー承認 2026-06-11 で確定)
- **工場数: 2(opus / sonnet、fresh)**。主題は「ECO/影響分析が BOM に宿り別工場へ転移するか」のため 1 工場では測定が弱い。haiku は入れない(能力差は前段で観測済み。Phase 7 の目的を濁さない)。
- 工場に渡すもの: **v0.2 個体のソース複製(factory-04 由来。各工場専用ディレクトリに設計者が事前複製・コミット=diff 基準点)+ 本 ECO + 改訂済み BOM(20/30–34/40)**。
- **渡さないもの(再確認)**: 設計対話 / S24–S25 の具体オラクル(41)/ 探索プローブ(42)/ **migration-oracle の実装** / **baseline fixture とその期待値(manifest)** / 他工場成果 / 旧 cheat-report。
- 指示: 「ECO の影響分析にある箇所**だけ**を改修せよ。影響なし箇所への変更は禁止」。
- **不要改変の測定**: 納品後に複製時点との git diff を取り、影響分析外への変更を分類して As-Built に記録:
  `format/noise`(整形・コメント等) / `test-only`(ハーネスのみ) / `behavior-risk`(挙動に影響しうる) / `contract-change`(公開契約の変更)。
- 自己受入: 既存ハーネス(unit+L1 スモーク)+ 追加 vectors(CP-CORE-LIMIT rev3 / CP-MEMBER-TYPE)。**赤のままの納品は不可(stop/report。nonconformance として As-Built に記録し、納品物として採点しない)**。

### 結果分類(製造前に固定)
| 観測 | 分類 |
|---|---|
| S01–S23 の失敗 | **regression** |
| S24–S25 の失敗 | **change miss** |
| M01–M04 の失敗 | **data-preservation miss** |
| 影響分析外への diff | **unnecessary modification**(上記4性質で細分) |
| 自己受入赤での停止 | **manufacturing nonconformance**(採点対象外) |

### 成功条件
2 fresh 工場が **S01–S25 と M01–M04 を通過**し、不要改変が `format/noise`・`test-only` に収まること。
失敗にも価値がある: 特に **S07 が壊れた場合は影響なし予測の反証**として最良級のデータ。

## 5. マイグレーション専用オラクル(M01–M04)
fixture(v0.2 個体で作成した実 DB)のコピーに対して rev3 ビルドを起動して検査:
| ID | 検査 | 失敗時の分類 |
|---|---|---|
| M01 | v0.2 スキーマの DB で rev3 が起動する(移行実行・エラーなし) | data-preservation miss |
| M02 | 既存の蔵書(copies/availableCopies)・会員・貸出一覧(件数・status・並び)が API から fixture manifest と同値で取得できる | data-preservation miss |
| M03 | 既存会員(active 2件保持)が 3冊目=201・4冊目=409(=既定 standard の上限が既存個体に適用) | change miss(移行後規則の適用漏れ) |
| M04 | 既存 returned 貸出の fineAmount が保持される | data-preservation miss |

## 6. 記録(製造・受入後に記入)
- 回帰(S01–S23): / 変更受入(S24–S25): / 移行(M01–M04): / 不要改変 diff: / 影響なし予測の的中:
