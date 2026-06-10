# forward-01.5 — マイグレーション専用オラクル(M01-M04。設計者側・工場非開示)
# v0.2 fixture のコピーに対して対象ビルドを起動し、データ保持と新ルール適用を検査する。
# 失敗の分類: M01/M02/M04 = data-preservation miss / M03 = change miss(ECO-001 §5)
param(
  [Parameter(Mandatory = $true)][string]$FactoryDir,
  [int]$Port = 5240,
  [string]$ResultFile = "result-migration.json"
)
$ErrorActionPreference = 'Stop'
$base = "http://127.0.0.1:$Port"
$fixDb = Join-Path $PSScriptRoot 'fixtures/baseline-v02.db'
$manifest = Get-Content (Join-Path $PSScriptRoot 'fixtures/baseline-v02-manifest.json') -Raw | ConvertFrom-Json -AsHashtable -DateKind String
if (-not (Test-Path $fixDb)) { throw "fixture not found: $fixDb" }

$apiProj = Join-Path $FactoryDir 'src/Library.Api'
dotnet build $apiProj -c Release --nologo -v q | Out-Host
if ($LASTEXITCODE -ne 0) { throw "build failed" }
$dll = Get-ChildItem -Recurse (Join-Path $apiProj 'bin/Release') -Filter 'Library.Api.dll' | Select-Object -First 1

# fixture を温存するため一時コピーで起動(移行は不可逆でよい=コピーが移行される)
$dbCopy = Join-Path ([System.IO.Path]::GetTempPath()) ("bomdd_mig_{0}_{1}.db" -f $Port, [guid]::NewGuid().ToString('N').Substring(0, 8))
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

# --- M01 起動(移行実行) ---
$ready = $false
for ($i = 0; $i -lt 80; $i++) {
  Start-Sleep -Milliseconds 250
  try { Invoke-WebRequest "$base/v1/books/bk_probe" -SkipHttpErrorCheck -TimeoutSec 2 | Out-Null; $ready = $true; break } catch {}
}
Add-Result 'M01' $ready 'v0.2 DB で起動(移行実行)' ("ready={0}" -f $ready)

if ($ready) {
  # --- M02 既存データ保持(manifest 突合) ---
  $bA = Invoke-Api GET "/v1/books/$($manifest.bookA.id)"
  $bB = Invoke-Api GET "/v1/books/$($manifest.bookB.id)"
  $l1 = Invoke-Api GET "/v1/loans?memberId=$($manifest.m1.id)"
  $l2 = Invoke-Api GET "/v1/loans?memberId=$($manifest.m2.id)"
  $okA = ($bA.status -eq 200) -and ($bA.body.copies -eq $manifest.bookA.copies) -and ($bA.body.availableCopies -eq $manifest.bookA.availableCopies)
  $okB = ($bB.status -eq 200) -and ($bB.body.copies -eq $manifest.bookB.copies) -and ($bB.body.availableCopies -eq $manifest.bookB.availableCopies)
  $items = @(); if ($l1.body -and $l1.body.ContainsKey('items')) { $items = @($l1.body.items) }
  $okL = ($l1.status -eq 200) -and ($items.Count -eq 3)
  if ($okL) {
    for ($i = 0; $i -lt 3; $i++) {
      $exp = $manifest.m1_loans_ordered[$i]
      if (([string]$items[$i].id -ne [string]$exp.id) -or ([string]$items[$i].status -ne [string]$exp.status)) { $okL = $false }
    }
  }
  $okL2 = ($l2.status -eq 200) -and (@($l2.body.items).Count -eq 0)
  $ok = $okA -and $okB -and $okL -and $okL2
  Add-Result 'M02' $ok 'books avail / m1 loans(3件・順序・status) / m2 空 = manifest 同値' ("A={0} B={1} m1={2} m2={3}" -f $okA, $okB, $okL, $okL2)

  # --- M04 既存 returned の fine 保持(M03 より先に読む=M03 は DB を変異させる) ---
  $retExp = $manifest.m1_loans_ordered[0]
  $retActual = $items | Where-Object { [string]$_.id -eq [string]$retExp.id } | Select-Object -First 1
  $ok = ($null -ne $retActual) -and ($retActual.fineAmount -eq $retExp.fineAmount)
  Add-Result 'M04' $ok ("returned fine={0} 保持" -f $retExp.fineAmount) ("fine={0}" -f $retActual.fineAmount)

  # --- M03 既存会員に既定 standard の上限適用(3冊目 201・4冊目 409) ---
  # 日付は既存貸出の期限(5/15・5/16)より前にする。較正(negative control)が
  # 当初の 6/20 案では判定順序3(延滞ブロック)が先に発火することを捕捉した(オラクル設計バグの事前検出)
  $r3 = Invoke-Api POST '/v1/loans' @{ bookId = $manifest.bookB.id; memberId = $manifest.m1.id; loanedAtUtc = '2026-05-10T09:00:00Z' }
  $r4 = Invoke-Api POST '/v1/loans' @{ bookId = $manifest.bookB.id; memberId = $manifest.m1.id; loanedAtUtc = '2026-05-10T10:00:00Z' }
  $code4 = ''; if ($r4.body -and $r4.body.error) { $code4 = [string]$r4.body.error.code }
  $ok = ($r3.status -eq 201) -and ($r4.status -eq 409) -and ($code4 -ceq 'loan_limit_exceeded')
  Add-Result 'M03' $ok '3rd=201, 4th=409/loan_limit_exceeded(既存会員=standard)' ("3rd={0} 4th={1}/{2}" -f $r3.status, $r4.status, $code4)
} else {
  foreach ($cid in @('M02', 'M04', 'M03')) { Add-Result $cid $false 'per ECO-001 §5' 'not-executed: app did not start' }
}

if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force; $proc.WaitForExit() }
$resultDir = Split-Path -Parent $ResultFile
if ($resultDir -and -not (Test-Path $resultDir)) { New-Item -ItemType Directory -Force $resultDir | Out-Null }
$passCount = @($results | Where-Object { $_.pass }).Count
@{ factory = $FactoryDir; oracle = 'migration M01-M04'; pass = $passCount; total = $results.Count; results = $results } |
  ConvertTo-Json -Depth 6 | Set-Content $ResultFile -Encoding utf8
Write-Host ("== MIGRATION RESULT: {0}/{1} -> {2} ==" -f $passCount, $results.Count, $ResultFile)
