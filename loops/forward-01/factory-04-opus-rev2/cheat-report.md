# Cheat Report — factory-04-opus-rev2 (forward-01)

製造装置: factory-04-opus-rev2
受入結果: `dotnet build Library.sln` 成功 / `dotnet run --project test/Library.Acceptance` = 25 PASS, 0 FAIL, exit 0
blocker: なし

BOM / K-BOM / Control Plan / 仕様(20-spec.md)から一意に導けなかった判断を全件記録する。
仕様で固定済みの事項(ID 形式・応答小数秒・null/欠落/空文字=400・internal_error 語彙など)は裁量ではないため対象外。

---

### CHEAT-F01-001 [error-message] エラー message の文面を装置裁量で決定
- 手法が与えなかったもの: K-ERROR-SCHEMA-001 は「message は非空、内容は自由」とのみ規定。具体文面の指示なし。
- 代替した判断: 各エラーに英語の短い説明文を固定(例 "no copies available" / "member has an overdue active loan" / "resource not found")。検査されるのは非空性のみなので任意。
- 重大度: minor

### CHEAT-F01-002 [sqlite-schema] 内部スキーマ(テーブル名・列名・型)を装置裁量で設計
- 手法が与えなかったもの: 仕様 §2.7・K-SQLITE-001 は「内部スキーマは自由(検査は API 挙動)」。具体的なテーブル定義なし。
- 代替した判断: books(id,title,copies) / members(id,name) / loans(id,book_id,member_id,loaned_at_utc,due_date_utc,returned_at_utc,fine_amount,status) の 3 テーブル。
  loaned_at_utc は正規化済み `yyyy-MM-ddTHH:mm:ssZ` 文字列、due_date_utc は `yyyy-MM-dd` 文字列、status は 'active'/'returned' のテキストで保存。availableCopies は派生値で持たず active loan の COUNT で都度算出。
- 重大度: minor

### CHEAT-F01-003 [sqlite-pragma] PRAGMA / WAL を採用しない判断
- 手法が与えなかったもの: K-SQLITE-001 は「WAL・PRAGMA は任意。採用したらずる報告」。
- 代替した判断: WAL も追加 PRAGMA も採用せず Microsoft.Data.Sqlite の既定設定のみ使用。接続はリクエストごとに開閉(K-SQLITE-001 準拠)。挙動契約に影響しないため既定で十分と判断。
- 重大度: minor

### CHEAT-F01-004 [transaction] トランザクション実装方式の選択
- 手法が与えなかったもの: K-SQLITE-001 は「書き込みは判定と更新を 1 トランザクション内」とのみ規定。実装手段は未指定。
- 代替した判断: 貸出作成・返却を `BeginTransaction()` 内で実行。同一トランザクション内で在庫/会員/active 件数のスナップショットを取得 → Library.Core の判定を呼ぶ → 許可時のみ INSERT/UPDATE して Commit、不許可時は Rollback。これにより INV-1/INV-2/INV-4 の判定と更新を原子化(同時要求の原子性自体は charter でスコープ外=検査されない)。
- 重大度: minor

### CHEAT-F01-005 [error-handling] 未ハンドル例外ミドルウェアの採用
- 手法が与えなかったもの: Work Order は「未ハンドル例外で 500 を返さない設計とする。防御ミドルウェアの採用は裁量。採用したらずる報告」。
- 代替した判断: `app.Use` で try/catch する最小ミドルウェアを 1 つ採用。例外時に共通封筒 + `code=internal_error`(rev2 語彙)で応答。なお受入では 500 発生自体が fail なので通常経路では到達しない安全網。
- 重大度: minor

### CHEAT-F01-006 [json-shape] 応答オブジェクトを Dictionary で構築(active のフィールド省略のため)
- 手法が与えなかったもの: K-JSON-001 は「active に returnedAtUtc/fineAmount を null でなく省略」と規定するが、System.Text.Json での実現手段は未指定。
- 代替した判断: 匿名型では status により出力キー集合を変えられないため、貸出応答を `Dictionary<string,object?>` で構築し、active 時はキー自体を入れない。仕様列挙フィールド以外の追加フィールドは出していない(追加フィールドゼロ)。
- 重大度: minor

### CHEAT-F01-007 [datetime-parse] 厳密パースの実装手段選択
- 手法が与えなかったもの: K-UTC-ISO8601-001 は「ParseExact 等で厳密検証」と方向は示すが具体 API は未指定。
- 代替した判断: `DateTimeOffset.TryParseExact` に 8 つの受理フォーマット('Z' をリテラル・小数秒 0〜7 桁)を与え、`DateTimeStyles.AssumeUniversal | AdjustToUniversal | None` を指定。リテラル 'Z' 固定マッチにより小文字 z・数値オフセット(+00:00 含む)・オフセット無し・日付のみを 400 で弾く。秒未満は秒精度に切り捨て。出力は `yyyy-MM-ddTHH:mm:ssZ` 固定。
- 重大度: minor

### CHEAT-F01-008 [parse-storage-roundtrip] 永続化済み日時の再パース方式
- 手法が与えなかったもの: 返却 fine 計算・一覧ソートで DB から読んだ loanedAtUtc 文字列を再び瞬時に戻す手段は未指定(内部スキーマ自由のため)。
- 代替した判断: loaned_at_utc は保存時点で正規化済み(秒精度・Z 付き)なので、読み出し時は出力フォーマット固定で `ParseExact`。due_date は `yyyy-MM-dd` の `DateOnly.ParseExact`。サーバ時計は一切使用しない。
- 重大度: minor

### CHEAT-F01-009 [smoke-harness] L1 スモークの実装詳細(ポート・起動方式・readiness 判定)
- 手法が与えなかったもの: M-BOM scope ② は「サブプロセス起動・全6エンドポイント正常系1本・期待 status 確認・停止」とのみ規定。ポート番号・起動コマンド・準備完了の判定方法は未指定。
- 代替した判断: `dotnet run --project src/Library.Api --no-build` を ASPNETCORE_URLS=http://127.0.0.1:5099・一時 LIBRARY_DB_PATH(GUID 名・終了時削除)で起動。任意エンドポイントが HTTP 応答を返したら ready とみなす(最大40秒ポーリング)。終了時は entireProcessTree で kill。これは検査治具側の選択で製品挙動には影響しない。
- 重大度: minor

### CHEAT-F01-010 [framework-detail] minimal API のサービス登録・Store ライフタイム
- 手法が与えなかったもの: M-BOM は「aspnetcore-minimal-api」とのみ指定。DI 登録方針・Store のライフタイムは未指定。
- 代替した判断: Store(接続文字列を保持するステートレスなファクトリ)を Singleton 登録。実接続は操作ごとに開閉するため Singleton 共有でもグローバル接続にはならない(K-SQLITE-001 の「プロセス内共有グローバル接続を使わない」に抵触しない)。
- 重大度: minor
