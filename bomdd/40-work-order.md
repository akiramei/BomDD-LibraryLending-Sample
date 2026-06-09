# Work Order — forward-01

## 目的
図書貸出 API(.NET 10 minimal API + SQLite)を製造する。

## 入力(これがすべて。これ以外を参照しない)
- `bomdd/20-spec.md`(仕様)
- `bomdd/30-ebom.yaml` / `31-kbom.yaml` / `32-mbom.yaml` / `33-control-plan.yaml` / `34-routing.yaml`

## 製造対象(配置は M-BOM artifact.path の通り)
- ルートに `Library.sln`(全プロジェクトを含む。target framework は **net10.0 で統一**)
- `src/Library.Core` — ドメイン判定の classlib
- `src/Library.Api` — ASP.NET Core minimal API。**listen アドレスは ASPNETCORE_URLS に従う。DB パスは LIBRARY_DB_PATH(未設定時 ./library.db)**。未ハンドル例外で 500 を返さない設計とする(防御ミドルウェアの採用は裁量。採用したらずる報告)
- `test/Library.Acceptance` — 自己受入コンソール。**必須範囲 = Control Plan の unit 行(CP-CORE-*)の test_vectors 全被覆**(Library.Core を直接呼ぶ。プロセス外形なし)。PASS/FAIL を標準出力し、FAIL があれば exit code 非0。L3 の自己被覆は任意(M-BOM scope 参照)

## 調達部品の規律
NuGet パッケージは `Microsoft.Data.Sqlite 9.0.0` **のみ**使用可。これ以外が必要だと判断した場合は採用せず標準ライブラリで代替し、判断をずる報告する。

## 必須受入(自己受入)
- リポジトリルートで `dotnet build Library.sln` が成功する
- `dotnet run --project test/Library.Acceptance` が全 PASS で exit code 0

## ずる報告(義務)
BOM/K-BOM/Control Plan から導けなかった判断は、**実装を止めずに**全件 `cheat-report.md` に記録する:
```
### CHEAT-F01-NNN [分類] 一行要約
- 手法が与えなかったもの:
- 代替した判断(何をどう埋めたか):
- 重大度: blocker / friction / minor
```
特に以下は判断したら必ず報告する:
- ID の接頭辞以降の形式・生成方式(K-ID-001 で実装裁量と明示された次元)
- 応答日時の小数秒桁・正規化方針
- 仕様列挙外の応答フィールドの追加
- SQLite の内部スキーマ・PRAGMA・トランザクション実装の選択
- エラー message の文面方針

## 進めない級の問題(blocker)を発見した場合
BOM の自己矛盾・実装不能を発見した場合は、当該製造単位を `blocked` とマークして他の単位を続行し、cheat-report に C6(手戻り)として記録して納品時に報告する。質問往復はしない。
