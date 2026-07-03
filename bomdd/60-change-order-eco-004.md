# Change Order — ECO-004(受入ハーネスの構成不一致の是正・harness_bug)

> 小 ECO(test-only・設計者適用)。対象は M-ACCEPTANCE-HARNESS-001 —
> 「acceptance harness も製品と同格」の規律に従い、ハーネス欠陥も ECO で是正・記録する。

## 0. 欠陥要求(帰属: harness_bug)
- **検出**: forward-02 の製造工場(opus)が自己受入中に検出・申告(2026-07-04。cheat-report /
  forward-02 report §4 副産物)。製品欠陥と区別して報告された=検出経路も正常動作。
- **観測**: `dotnet build Library.sln -c Release` のみのビルド後、Release ハーネスを実行すると
  L1 スモークが「API process became ready」で **FAIL(31/1)**。
- **根因**: SmokeChecks が API 子プロセスを `dotnet run --project src/Library.Api --no-build` で
  起動しており、`dotnet run` は**構成無指定だと -c Debug に暗黙既定**される。Release のみの
  ビルドでは Debug バイナリが不在=ハーネス自身の構成と API 起動構成の不一致(偽赤)。
- **再現(是正前・negative control)**: bin/obj クリーン → Release のみビルド → Release ハーネス
  直接実行 → **31/1(became ready FAIL)** — 工場実測と同一プロファイルを確認(2026-07-04)。
- baseline: v0.5(part-swap-01)+ ECO-003 完了時 HEAD。

## 1. 処方
- `dotnet run --no-build` をやめ、**最後にビルドされた Library.Api.dll を直接起動**する
  (bin/ 配下の最新 write-time の dll。ref/refint=参照アセンブリは除外)。
  構成非依存(Debug/Release どちらでも・両構成併存時は最新優先)= 設計者側治具
  (oracle/fixed-oracle.ps1)と同型の起動方式に揃えた。
- 変更ファイル: **test/Library.Acceptance/SmokeChecks.cs のみ**(製品 src・オラクル・BOM 挙動契約は不変)。

## 2. 受入(2026-07-04)
| 経路 | 結果 |
|---|---|
| ① Release のみビルド → Release ハーネス(旧・偽赤の組み合わせ) | **37/37** |
| ② Debug 既定(`dotnet run --project test/Library.Acceptance`) | 37/37 |
| ③ 両構成併存(Release を後からビルド=最新優先の確認) | 37/37 |
| 回帰: 固定オラクル S01–S28(製品不変のアンカー) | 28/28 |

- diff: SmokeChecks.cs のみ(test-only)。製品 .cs・csproj 不変。
- 工程注記: forward-02 の工場向け自己受入指示「`dotnet build -c Release` が警告 0 で通る」+
  「既存ハーネスを実行して全緑」の組み合わせが本欠陥を踏む導線だった — 是正後はどの構成の
  ビルドでもハーネスが自走する(work order 側の指示変更は不要)。
