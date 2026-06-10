# forward-01.5 — v0.2 baseline DB fixture 作成(設計者側・再現可能化のため script 化)
# As-Maintained 個体 = factory-04-opus-rev2 のビルドで実データを作り、
# oracle/fixtures/baseline-v02.db + baseline-v02-manifest.json として凍結する。
param(
  [string]$BaselineDir = (Join-Path $PSScriptRoot '..\loops\forward-01\factory-04-opus-rev2'),
  [int]$Port = 5230
)
$ErrorActionPreference = 'Stop'
$base = "http://127.0.0.1:$Port"
$fixDir = Join-Path $PSScriptRoot 'fixtures'
New-Item -ItemType Directory -Force $fixDir | Out-Null
$dbPath = Join-Path $fixDir 'baseline-v02.db'
foreach ($f in @($dbPath, "$dbPath-wal", "$dbPath-shm")) { if (Test-Path $f) { Remove-Item -Force $f } }

$apiProj = Join-Path $BaselineDir 'src/Library.Api'
dotnet build $apiProj -c Release --nologo -v q | Out-Host
$dll = Get-ChildItem -Recurse (Join-Path $apiProj 'bin/Release') -Filter 'Library.Api.dll' | Select-Object -First 1
$env:LIBRARY_DB_PATH = $dbPath
$env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
$proc = Start-Process dotnet -ArgumentList @($dll.FullName) -PassThru -WindowStyle Hidden -WorkingDirectory (Split-Path $dll.FullName)
for ($i = 0; $i -lt 80; $i++) { Start-Sleep -Milliseconds 250; try { Invoke-WebRequest "$base/v1/books/bk_x" -SkipHttpErrorCheck -TimeoutSec 2 | Out-Null; break } catch {} }

function Post([string]$Path, $Body) {
  $r = Invoke-WebRequest -Method POST -Uri "$base$Path" -Body ($Body | ConvertTo-Json) -ContentType 'application/json' -SkipHttpErrorCheck
  return $r.Content | ConvertFrom-Json -AsHashtable -DateKind String
}
# シナリオ: 蔵書2・会員2。M1 = returned 1件(fine=100)+ active 2件。M2 = 貸出なし
$bookA = Post '/v1/books' @{ title = 'Baseline Book A'; copies = 3 }
$bookB = Post '/v1/books' @{ title = 'Baseline Book B'; copies = 5 }
$m1 = Post '/v1/members' @{ name = 'Legacy Member One' }
$m2 = Post '/v1/members' @{ name = 'Legacy Member Two' }
$loanOld = Post '/v1/loans' @{ bookId = $bookA.id; memberId = $m1.id; loanedAtUtc = '2026-04-01T09:00:00Z' }   # due 4/15
$ret = Post "/v1/loans/$($loanOld.id)/return" @{ returnedAtUtc = '2026-04-16T10:00:00Z' }                        # fine 100
$loanA = Post '/v1/loans' @{ bookId = $bookA.id; memberId = $m1.id; loanedAtUtc = '2026-05-01T09:00:00Z' }       # active
$loanB = Post '/v1/loans' @{ bookId = $bookB.id; memberId = $m1.id; loanedAtUtc = '2026-05-02T09:00:00Z' }       # active

Stop-Process -Id $proc.Id -Force; $proc.WaitForExit()

$manifest = @{
  created_from = 'factory-04-opus-rev2 (v0.2-forward-01-rev2)'
  bookA = @{ id = $bookA.id; copies = 3; availableCopies = 2 }   # active 1件(loanA)
  bookB = @{ id = $bookB.id; copies = 5; availableCopies = 4 }   # active 1件(loanB)
  m1 = @{ id = $m1.id; active_loans = 2 }
  m2 = @{ id = $m2.id; active_loans = 0 }
  m1_loans_ordered = @(                                           # §2.6 loanedAt 昇順
    @{ id = $loanOld.id; status = 'returned'; fineAmount = 100 },
    @{ id = $loanA.id; status = 'active' },
    @{ id = $loanB.id; status = 'active' }
  )
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $fixDir 'baseline-v02-manifest.json') -Encoding utf8
Write-Host "fixture ready: $dbPath / fine(returned)=$($ret.fineAmount)"
