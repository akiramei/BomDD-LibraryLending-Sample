# forward-02(ECO-002)— v0.3 baseline DB fixture 作成(設計者側・再現可能化のため script 化)
# As-Maintained 個体 = factory-eco-01-opus(v0.3-forward-015)のビルドで実データを作り、
# oracle/fixtures/baseline-v03.db + baseline-v03-manifest.json として凍結する。
# 効力オラクル E01-E04 の入力: premium 会員の active 貸出(旧規則 +14日)を含むのが要点。
param(
  [string]$BaselineDir = (Join-Path $PSScriptRoot '..\loops\forward-015\factory-eco-01-opus'),
  [int]$Port = 5250
)
$ErrorActionPreference = 'Stop'
$base = "http://127.0.0.1:$Port"
$fixDir = Join-Path $PSScriptRoot 'fixtures'
New-Item -ItemType Directory -Force $fixDir | Out-Null
$dbPath = Join-Path $fixDir 'baseline-v03.db'
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
  if ([int]$r.StatusCode -ge 400) { throw "seed failed: $Path -> $($r.Content)" }
  return $r.Content | ConvertFrom-Json -AsHashtable -DateKind String
}
# シナリオ: 蔵書2。標準会員 mStd = returned 1件(fine=100)+ active 1件 / premium 会員 mPrem = active 2件(旧規則 +14日)
$bookA = Post '/v1/books' @{ title = 'Effectivity Book A'; copies = 3 }
$bookB = Post '/v1/books' @{ title = 'Effectivity Book B'; copies = 5 }
$mStd  = Post '/v1/members' @{ name = 'Effectivity Std' }                            # standard
$mPrem = Post '/v1/members' @{ name = 'Effectivity Prem'; memberType = 'premium' }   # premium
$stdOld    = Post '/v1/loans' @{ bookId = $bookA.id; memberId = $mStd.id;  loanedAtUtc = '2026-04-01T09:00:00Z' }   # due 4/15
$stdRet    = Post "/v1/loans/$($stdOld.id)/return" @{ returnedAtUtc = '2026-04-16T10:00:00Z' }                       # fine 100
$stdActive = Post '/v1/loans' @{ bookId = $bookA.id; memberId = $mStd.id;  loanedAtUtc = '2026-05-01T09:00:00Z' }   # active, due 5/15
$premA1    = Post '/v1/loans' @{ bookId = $bookA.id; memberId = $mPrem.id; loanedAtUtc = '2026-05-01T09:00:00Z' }   # active, v0.3=+14 -> due 5/15
$premA2    = Post '/v1/loans' @{ bookId = $bookB.id; memberId = $mPrem.id; loanedAtUtc = '2026-05-02T09:00:00Z' }   # active, v0.3=+14 -> due 5/16(E04 で返却)

Stop-Process -Id $proc.Id -Force; $proc.WaitForExit()

$manifest = @{
  created_from = 'factory-eco-01-opus (v0.3-forward-015)'
  bookA = @{ id = $bookA.id; copies = 3; availableCopies = 1 }   # active 2件(stdActive, premA1)
  bookB = @{ id = $bookB.id; copies = 5; availableCopies = 4 }   # active 1件(premA2)
  mStd  = @{ id = $mStd.id;  memberType = 'standard'; active_loans = 1 }
  mPrem = @{ id = $mPrem.id; memberType = 'premium';  active_loans = 2 }
  mStd_loans_ordered = @(                                          # §2.6 loanedAt 昇順
    @{ id = $stdOld.id;    status = 'returned'; fineAmount = 100 },
    @{ id = $stdActive.id; status = 'active';   dueDateUtc = $stdActive.dueDateUtc }
  )
  mPrem_loans_ordered = @(                                         # 旧規則 +14日の確定値(E02 の正)
    @{ id = $premA1.id; status = 'active'; dueDateUtc = $premA1.dueDateUtc },
    @{ id = $premA2.id; status = 'active'; dueDateUtc = $premA2.dueDateUtc }
  )
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $fixDir 'baseline-v03-manifest.json') -Encoding utf8
Write-Host ("fixture ready: {0} / premA1 due={1} premA2 due={2}(旧規則 +14 を期待)" -f $dbPath, $premA1.dueDateUtc, $premA2.dueDateUtc)
