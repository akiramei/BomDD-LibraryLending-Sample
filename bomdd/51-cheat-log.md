# Cheat Log — forward-01(設計者側 統合台帳)

> 工場別の全件は各 `loops/forward-01/factory-*/cheat-report.md`。本台帳は横断分析と、**製造側(factory)/検査器側(harness)/手法側(process)** の分離記録。

## 製造側(factory)— 件数と分布
| 工場 | 件数 | blocker | 主な内容 |
|---|---|---|---|
| factory-01 (opus) | 9 | 0 | ID=GUID32 / 応答日時=秒精度正規化 / 追加フィールドなし / スキーマ・PRAGMA選択 / 防御MW / JSON型検証の厳密化 / message英文 |
| factory-02 (sonnet) | 8 | 0 | ID=GUID32 / 小数秒0正規化 / TEXT保存 / WALなし / message英文 / 防御MW(500時code=internal_error) / null vs 空文字の解釈 / 追加フィールドなし |
| factory-03 (haiku) | 3 | 0 | ID=連番10桁 / 応答日時=7桁("O") / PRAGMA既定 |

**観察**: 全工場の cheat は、設計時に exploratory / 裁量と**宣言済みの次元に正確に収まった**(宣言外の沈黙への手伸ばし=unspecified_bom_residue は固定オラクル層で 0 件)。K-BOM 事前抽出(playbook §4.3)が分岐候補を先回りで固定した効果と整合。

### 注目すべき個別件
- **CHEAT-F01-007(sonnet)**: 「フィールド欠落(null)=400 / 空文字=404」の境界解釈。仕様は §2.4/§2.6 から導出可能だが「null はどちらか」を明文化していなかった。3工場の挙動は一致(S03/S04 PASS)したため残渣にはならなかったが、**次版仕様で明文化すべき**(質問リスト Q4)。
- **CHEAT-F01-006(sonnet)**: 防御 MW の 500 時 code=`internal_error`(列挙外)。仕様「500は契約外」の解釈として妥当だが、列挙の「これ以外を返さない」と緊張関係。**仕様の精密化候補**(質問リスト Q5)。

## 検査器側(harness)
### CHEAT-F01-H001 [observer_representation_coupling] ConvertFrom-Json の暗黙 DateTime 変換
- side: **harness** / 発生段: ⑥合否(初回採点)
- 内容: 治具が `ConvertFrom-Json` の既定挙動により ISO-8601 文字列を DateTime オブジェクトへ暗黙変換し、文字列キャストで瞬時情報が破壊され S05 を誤判定(opus を 19/20 と誤採点)。
- 補正: `-DateKind String` で raw 文字列を保持し明示パースに変更 → opus 20/20。
- 意義: **C2(共有暗黙知)が検査器に出る v1.2 のパターンが、フォワード・モードでも初回採点で再発**(3例目: saga map/array・saga camelCase に続く)。「オラクルは付随表現でなく契約セマンティクスを見る」は治具のデシリアライザ選択にまで及ぶ。
### H-002(改善・ずるではない)
- シード失敗時に治具が中断し結果ファイルを残さなかった → 残ケースを not-executed(fail) として記録し完走する頑健化を実施(haiku 採点で必要になった)。

## 手法側(process)
### CHEAT-F01-P001 [手法逸脱] G2 再監査の省略
- 仕様 rev1 への補正後、playbook §3.2 は「補正して再監査」を求めるが、コスト判断で G3 ドライランによる代替とした。G3 で曖昧起因の質問が 7 件出たが全て補正済みで残ゼロ。**省略の影響は観測範囲では出なかった**が、規律からの逸脱として記録。
### CHEAT-F01-P002 [手法の盲点候補] 自己受入の必須範囲が unit 行のみ
- M-BOM scope(G3 裁定)で自己受入必須範囲=CP-CORE unit 行のみとしたため、**haiku は自己受入 17/17 緑のまま API 表面が実行時全滅**(貸出エンドポイントが常時 400)した。opus は任意のスモークを自主実施して捕捉、haiku は実施せず盲点に。
- 手法的修正候補: **自己受入の必須範囲に最低限の L1 API スモーク(起動+各エンドポイント1本)を含める**(playbook/M-BOM テンプレへ反映候補)。「受入の被覆が捕捉を決める」(webapi-01)の自己受入版。
