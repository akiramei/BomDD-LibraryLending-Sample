# Cheat Report — factory-eco-02-sonnet (ECO-001)

工場ID: factory-eco-02-sonnet  
フェーズ: ECO-001 (forward-01.5 部分再製造)  
様式: work order §ずる報告

---

### CHEAT-F015-E001 [手段選択] user_version PRAGMA によるスキーマバージョン管理

- 手法が与えなかったもの: K-SQLITE-001 は「schema バージョンを DB 内で管理し(user_version PRAGMA か version テーブル。選択は裁量・ずる報告)」と明記。どちらを使うかはずる報告指定。
- 代替した判断(何をどう埋めたか): `PRAGMA user_version` を採用。v0.2 DB は user_version = 0 のため、テーブル存在チェック(sqlite_master)と列存在チェック(pragma_table_info)の組み合わせで v0.2 を識別し、ALTER TABLE 後に user_version = 3 を設定する。version テーブルより軽量かつ依存関係がないため選択。
- 重大度: minor

---

### CHEAT-F015-E002 [手段選択] v0.2 DB 検出ロジック(user_version=0 + membersテーブル存在 + member_type列不在)

- 手法が与えなかったもの: 仕様§2.7(rev3)は「v0.2 スキーマ(As-Maintained 個体=factory-04)の DB を検出した場合、自動移行する」と記述するが、検出基準は規定しない。As-Maintained 個体 = factory-04 の内部スキーマは非開示。
- 代替した判断(何をどう埋めたか): 作業ディレクトリにある v0.2 ソース(本工場の Store.cs 複製)を読んで旧スキーマを確認。members テーブルに member_type 列がないことが v0.2 の証拠。user_version=0(=未設定)かつ members テーブルが存在し member_type 列が存在しない = v0.2 DBと判定。この組み合わせは「工場が自身のソースから読み取れる旧スキーマ」を根拠とするため、BOM 禁止(oracle/ ディレクトリ・他工場成果)は参照していない。
- 重大度: minor

---

### CHEAT-F015-E003 [手段選択] LoanContext.MemberType のデフォルト値を "standard" に設定

- 手法が与えなかったもの: LoanContext は既存の record struct。BOM は MemberType フィールドの追加を要求するが、既存コードとの互換（テスト等）のためデフォルト値を与えるかどうかは明示されていない。
- 代替した判断(何をどう埋めたか): C# のオプショナルパラメータ(default = "standard")を採用。既存のテストコードが MemberType なしで LoanContext を構築しているためコンパイルエラーを避けつつ、既定 standard の意味と一致させた。
- 重大度: minor

---

### CHEAT-F015-E004 [自己検証方式] スキーマ移行の自己検証を PRAGMA user_version の読み返しで行う

- 手法が与えなかったもの: 仕様§2.7(rev3)は「移行の正否も挙動(専用オラクル M01–M04)で検査する」と記述し、自己検証方法は工場裁量。
- 代替した判断(何をどう埋めたか): EnsureSchema() 内で ALTER TABLE 実行後に PRAGMA user_version = 3 を設定し、起動時に user_version を読み返すことで「v0.2 から移行済み」の状態が DB に永続される設計とした。外部から確認する場合は migration-oracle(M01–M04)が本番の検証手段。自己受入では L1 スモークが POST /v1/members と POST /v1/loans を通し、memberType = "standard" で動作することを確認している。
- 重大度: minor

---

## blocker 有無

なし。BOM から導けなかった判断はすべて minor。
