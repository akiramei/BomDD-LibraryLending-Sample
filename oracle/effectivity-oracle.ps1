# forward-02(ECO-002)— 効力(effectivity)専用オラクル(E01-E04。設計者側・工場非開示)
# v0.3 fixture のコピーに対して対象ビルドを起動し、「既存貸出への遡及なし+新規のみ新規則」を検査する。
# 失敗の分類: E01/E02/E04 = data-preservation miss(効力違反) / E03 = change miss(ECO-002 §5)
# 検査順序は E01→E02→E03→E04 固定(E03/E04 は DB を変異させるため、同値検査を先に読む)。
param(
  [Parameter(Mandatory = $true)][string]$FactoryDir,
  [int]$Port = 5260,
  [string]$ResultFile = "result-effectivity.json"
)
$ErrorActionPreference = 'Stop'
$base = "http://127.0.0.1:$Port"
$fixDb = Join-Path $PSScriptRoot 'fixtures/baseline-v03.db'
$manifest = Get-Content (Join-Path $PSScriptRoot 'fixtures/baseline-v03-manifest.json') -Raw | ConvertFrom-Json -AsHashtable -DateKind String
if (-not (Test-Path $fixDb)) { throw "fixture not found: $fixDb" }

$apiProj = Join-Path $FactoryDir 'src/Library.Api'
dotnet build $apiProj -c Release --nologo -v q | Out-Host
if ($LASTEXITCODE -ne 0) { throw "build failed" }
$dll = Get-ChildItem -Recurse (Join-Path $apiProj 'bin/Release') -Filter 'Library.Api.dll' | Select-Object -First 1

# fixture を温存するため一時コピーで起動
$dbCopy = Join-Path ([System.IO.Path]::GetTempPath()) ("bomdd_eff_{0}_{1}.db" -f $Port, [guid]::NewGuid().ToString('N').Substring(0, 8))
Copy-Item $fixDb $dbCopy -Force
$env:LIBRARY_DB_PATH = $dbCopy
$env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
$proc = Start-Process dotnet -ArgumentList @($dll.FullName) -PassThru -WindowStyle Hidden -WorkingDirectory (Split-Path $dll.FullName)

$results = New-Object System.Collections.ArrayList
function Add-Result { param([string]$Id, [bool]$Pass, [string]$Expected, [string]$Actual)
  [void]$results.Add(@{ id = $Id; pass = $Pass; expected = $Expected; actual = $Actual })
  $mark = '+ PASS'; if (-not $Pass) { $mark = 'x FAIL' }
  Write-Host ("  [{0}] {1}  exp: {2}  act: {3}" -f $Id, $mark, $Expected, $Actual)
}
function Invoke-Api { param([string]$Method, [string]$Path, $Body = $null)
  $p = @{ Method = $Method; Uri = "$base$Path"; SkipHttpErrorCheck = $true; TimeoutSec = 20 }
  if ($null -ne $Body) { $p.Body = ($Body | ConvertTo-Json -Depth 5); $p.ContentType = 'application/json' }
  $r = Invoke-WebRequest @p
  $json = $null
  if ($r.Content) { try { $json = $r.Content | ConvertFrom-Json -AsHashtable -DateKind String } catch {} }
  return @{ status = [int]$r.StatusCode; body = $json; raw = [string]$r.Content }
}

# --- 起動待ち ---
$ready = $false
for ($i = 0; $i -lt 80; $i++) {
  Start-Sleep -Milliseconds 250
  try { Invoke-WebRequest "$base/v1/books/bk_probe" -SkipHttpErrorCheck -TimeoutSec 2 | Out-Null; $ready = $true; break } catch {}
}

if ($ready) {
  # --- E01 既存データ保持(manifest 突合) ---
  $bA = Invoke-Api GET "/v1/books/$($manifest.bookA.id)"
  $bB = Invoke-Api GET "/v1/books/$($manifest.bookB.id)"
  $lStd = Invoke-Api GET "/v1/loans?memberId=$($manifest.mStd.id)"
  $lPrem = Invoke-Api GET "/v1/loans?memberId=$($manifest.mPrem.id)"
  $okA = ($bA.status -eq 200) -and ($bA.body.copies -eq $manifest.bookA.copies) -and ($bA.body.availableCopies -eq $manifest.bookA.availableCopies)
  $okB = ($bB.status -eq 200) -and ($bB.body.copies -eq $manifest.bookB.copies) -and ($bB.body.availableCopies -eq $manifest.bookB.availableCopies)
  function Test-LoanList { param($Res, $Expected)
    if ($Res.status -ne 200 -or -not $Res.body -or -not $Res.body.ContainsKey('items')) { return $false }
    $items = @($Res.body.items)
    if ($items.Count -ne @($Expected).Count) { return $false }
    for ($i = 0; $i -lt $items.Count; $i++) {
      $exp = $Expected[$i]
      if (([string]$items[$i].id -ne [string]$exp.id) -or ([string]$items[$i].status -ne [string]$exp.status)) { return $false }
    }
    return $true
  }
  $okStd = Test-LoanList $lStd $manifest.mStd_loans_ordered
  $okPrem = Test-LoanList $lPrem $manifest.mPrem_loans_ordered
  # returned の fine 保持(mStd の1件目)
  $retExp = $manifest.mStd_loans_ordered[0]
  $retActual = @($lStd.body.items) | Where-Object { [string]$_.id -eq [string]$retExp.id } | Select-Object -First 1
  $okFine = ($null -ne $retActual) -and ($retActual.fineAmount -eq $retExp.fineAmount)
  $ok = $okA -and $okB -and $okStd -and $okPrem -and $okFine
  Add-Result 'E01' $ok 'books avail / 両会員の loans(件数・順序・status)/ returned fine = manifest 同値' ("A={0} B={1} std={2} prem={3} fine={4}" -f $okA, $okB, $okStd, $okPrem, $okFine)

  # --- E02 既存 premium 貸出の dueDateUtc 保持(遡及なし。旧規則 +14日の確定値) ---
  $premItems = @($lPrem.body.items)
  $e2ok = $true; $e2act = @()
  foreach ($exp in $manifest.mPrem_loans_ordered) {
    $act = $premItems | Where-Object { [string]$_.id -eq [string]$exp.id } | Select-Object -First 1
    $due = ''; if ($act) { $due = [string]$act.dueDateUtc }
    $e2act += $due
    if (-not $act -or ($due -cne [string]$exp.dueDateUtc)) { $e2ok = $false }
  }
  Add-Result 'E02' $e2ok ("existing premium dues = {0}(+14日のまま)" -f (@($manifest.mPrem_loans_ordered | ForEach-Object { $_.dueDateUtc }) -join ',')) ($e2act -join ',')

  # --- E03 既存 premium 会員の新規貸出は +21日(新規則は新規のみ) ---
  $e3 = Invoke-Api POST '/v1/loans' @{ bookId = $manifest.bookB.id; memberId = $manifest.mPrem.id; loanedAtUtc = '2026-05-10T09:00:00Z' }
  $ok = ($e3.status -eq 201) -and ([string]$e3.body.dueDateUtc -ceq '2026-05-31')
  Add-Result 'E03' $ok '201 / dueDateUtc=2026-05-31(5/10+21日)' ("{0} due={1}" -f $e3.status, $e3.body.dueDateUtc)

  # --- E04 既存 premium 貸出の返却料金は確定済み dueDateUtc 基準(FMEA-006 狙い撃ち) ---
  # premA2(due=旧規則の確定値)を「その due+1日」で返却 → fine=100。
  # 実装が期間日数を再計算する(loanedAt+21)と due が後ろへずれ fine=0 になる=検出。
  $premA2 = $manifest.mPrem_loans_ordered[1]
  $dueA2 = [datetime]::ParseExact([string]$premA2.dueDateUtc, 'yyyy-MM-dd', [cultureinfo]::InvariantCulture)
  $retAt = $dueA2.AddDays(1).ToString('yyyy-MM-dd') + 'T00:00:00Z'
  $e4 = Invoke-Api POST "/v1/loans/$($premA2.id)/return" @{ returnedAtUtc = $retAt }
  $ok = ($e4.status -eq 200) -and ($e4.body.fineAmount -eq 100)
  Add-Result 'E04' $ok ("200 / fine=100(確定 due {0} の翌日返却)" -f $premA2.dueDateUtc) ("{0} fine={1}" -f $e4.status, $e4.body.fineAmount)
} else {
  Add-Result 'E01' $false 'v0.3 DB で起動' 'not-executed: app did not start'
  foreach ($cid in @('E02', 'E03', 'E04')) { Add-Result $cid $false 'per ECO-002 §5' 'not-executed: app did not start' }
}

if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force; $proc.WaitForExit() }
$resultDir = Split-Path -Parent $ResultFile
if ($resultDir -and -not (Test-Path $resultDir)) { New-Item -ItemType Directory -Force $resultDir | Out-Null }
$passCount = @($results | Where-Object { $_.pass }).Count
@{ factory = $FactoryDir; oracle = 'effectivity E01-E04'; pass = $passCount; total = $results.Count; results = $results } |
  ConvertTo-Json -Depth 6 | Set-Content $ResultFile -Encoding utf8
Write-Host ("== EFFECTIVITY RESULT: {0}/{1} -> {2} ==" -f $passCount, $results.Count, $ResultFile)
