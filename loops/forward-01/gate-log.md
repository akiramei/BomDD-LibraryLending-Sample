# forward-01 ゲート記録

## G0 チャーター(2026-06-10)
固定項目(工場構成=研究3工場 opus/sonnet/haiku・収束予算2・役割・境界=wire)完備。PASS。

## G1 根拠精度(2026-06-10)
REQ-001〜007+NFR-001、全件 rationale_precision: ok(needs-refinement ゼロ)。PASS。
Limitation: PO 役は設計者が代行(charter 参照)。

## G2 マルチリーダー仕様監査(2026-06-10)
リーダー3体(opus/sonnet/haiku、互いに非開示・仕様 rev0 のみ供与)。
- 要求・不変条件の抽出: 3体一致(振る舞いは一意に読めた)
- 曖昧指摘: **opus 17 / sonnet 13 / haiku 17 件**
- 3体が重ねた指摘(優先固定): +00:00 / ISO許容形 / 返却過去判定の粒度 / id昇順の定義 / 延滞any/all / 一覧の「同形」
- 単独採用: 未来日時受理(sonnet)・500は契約外(opus)・同一returnedAtUtc再送も409(haiku)・message非空(sonnet)
- 処置: 仕様 rev1 で全件 固定 or 明示的 exploratory/out-of-scope 化。再監査は省略し G3 で代替(コスト判断=手法からの逸脱として cheat-log に記録)
- **観察**: Loop7「振る舞いは一致し根拠の精度で割れる」がフォワード仕様でも再現。PASS(条件付き)。

## G2' MeasurementCapability(2026-06-10)
全 REQ/NFR = adequate。例外: INV-1 同時要求の原子性 = **insufficient-depth**(charter でスコープ外宣言済み・次段へ)。unmeasurable ゼロ。PASS。

## G3 ドライラン(2026-06-10, fresh sonnet)
着手可否 = yes。質問: blocker 1(NFR-001 の REQ 帰属表記)/ friction 5(.sln 配置・自己受入の起動方式・build 対象・TFM・500 方針)/ minor 1(空文字 memberId)。矛盾 2(NFR 帰属表記・SQLite 9.0.0 と .NET 10 の組合せ注記)。
- 処置(全件裁定・補正済み): NFR 帰属を仕様 §5 で明確化 / Library.sln をルートに+net10.0 統一(M-BOM solution 節)/ 自己受入の必須範囲=unit 行のみ・L3 は任意(M-BOM scope)/ 500 を返さない設計+防御は裁量(work order)/ 空文字 memberId=404(仕様 §2.6)/ 9.0.0 固定は意図的(procurement 注記)。
- **観察**: 実装裁量と明示した次元(ID形式)は質問に出なかった=「宣言」が質問を吸収する。残る質問ゼロ(全て specified 化)。PASS。
