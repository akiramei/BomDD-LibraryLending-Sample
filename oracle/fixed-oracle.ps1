# BomDD forward-01 — 固定オラクル実行治具(設計者側)
# 使い方: pwsh oracle/fixed-oracle.ps1 -FactoryDir <factory root> -Port 5211 -ResultFile result-opus.json
# 期待値の典拠は bomdd/41-fixed-oracle.yaml(=仕様 20-spec.md rev1)。
param(
  [Parameter(Mandatory = $true)][string]$FactoryDir,
  [int]$Port = 5210,
  [string]$ResultFile = "result.json"
)
$ErrorActionPreference = 'Stop'
$base = "http://127.0.0.1:$Port"
$apiProj = Join-Path $FactoryDir 'src/Library.Api'
if (-not (Test-Path $apiProj)) { throw "src/Library.Api not found under $FactoryDir" }

Write-Host "== build =="
dotnet build $apiProj -c Release --nologo -v q | Out-Host
if ($LASTEXITCODE -ne 0) { throw "build failed" }
$dll = Get-ChildItem -Recurse (Join-Path $apiProj 'bin/Release') -Filter 'Library.Api.dll' | Select-Object -First 1
if (-not $dll) { throw "Library.Api.dll not found" }

$dbPath = Join-Path ([System.IO.Path]::GetTempPath()) ("bomdd_lend_{0}_{1}.db" -f $Port, [guid]::NewGuid().ToString('N').Substring(0, 8))
foreach ($f in @($dbPath, "$dbPath-wal", "$dbPath-shm")) { if (Test-Path $f) { Remove-Item -Force $f } }

$script:apiProc = $null
function Start-Api {
  $env:LIBRARY_DB_PATH = $dbPath
  $env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
  $env:ASPNETCORE_ENVIRONMENT = 'Production'
  $script:apiProc = Start-Process -FilePath 'dotnet' -ArgumentList @($dll.FullName) -PassThru -WindowStyle Hidden -WorkingDirectory (Split-Path $dll.FullName)
  $ready = $false
  for ($i = 0; $i -lt 80; $i++) {
    Start-Sleep -Milliseconds 250
    try {
      Invoke-WebRequest -Uri "$base/v1/books/bk_readiness_probe" -SkipHttpErrorCheck -TimeoutSec 2 | Out-Null
      $ready = $true; break
    } catch {}
  }
  if (-not $ready) { throw "API did not become ready on $base" }
}
function Stop-Api {
  if ($script:apiProc -and -not $script:apiProc.HasExited) {
    Stop-Process -Id $script:apiProc.Id -Force
    $script:apiProc.WaitForExit()
  }
}

function Invoke-Api {
  param([string]$Method, [string]$Path, $Body = $null)
  $p = @{ Method = $Method; Uri = "$base$Path"; SkipHttpErrorCheck = $true; TimeoutSec = 20 }
  if ($null -ne $Body) { $p.Body = ($Body | ConvertTo-Json -Depth 6); $p.ContentType = 'application/json' }
  $r = Invoke-WebRequest @p
  $json = $null
  if ($r.Content) { try { $json = $r.Content | ConvertFrom-Json -AsHashtable } catch {} }
  return @{ status = [int]$r.StatusCode; body = $json; raw = [string]$r.Content }
}

$script:results = New-Object System.Collections.ArrayList
function Add-Result { param([string]$Id, [bool]$Pass, [string]$Expected, [string]$Actual)
  [void]$script:results.Add(@{ id = $Id; pass = $Pass; expected = $Expected; actual = $Actual })
  $mark = '+ PASS'; if (-not $Pass) { $mark = 'x FAIL' }
  Write-Host ("  [{0}] {1}  exp: {2}  act: {3}" -f $Id, $mark, $Expected, $Actual)
}
function Test-ErrorShape { param($Res, [int]$Status, [string]$Code)
  $ok = ($Res.status -eq $Status)
  $code = ''
  if ($Res.body -and $Res.body.ContainsKey('error') -and $Res.body.error) {
    $code = [string]$Res.body.error.code
    $msg = [string]$Res.body.error.message
    $ok = $ok -and ($code -eq $Code) -and (-not [string]::IsNullOrEmpty($msg))
  } else { $ok = $false }
  return @{ ok = $ok; desc = "$($Res.status)/$code" }
}
function Test-Instant { param([string]$A, [string]$B)
  try {
    $da = [datetimeoffset]::Parse($A, [cultureinfo]::InvariantCulture)
    $db2 = [datetimeoffset]::Parse($B, [cultureinfo]::InvariantCulture)
    return ($da.UtcTicks -eq $db2.UtcTicks)
  } catch { return $false }
}

$probes = @{ ids = @(); datetimeEcho = @(); extraFields = @{}; errorMessages = @(); latencyMs = $null }
function Record-Extra { param([string]$Where, $Body, [string[]]$Known)
  if ($null -eq $Body) { return }
  $extra = @($Body.Keys | Where-Object { $Known -notcontains $_ })
  if ($extra.Count -gt 0) { $probes.extraFields[$Where] = $extra }
}

Start-Api
Write-Host "== fixed oracle cases =="

# --- S01 蔵書登録 ---
$rBook1 = Invoke-Api POST '/v1/books' @{ title = 'Refactoring'; copies = 2 }
$ok = ($rBook1.status -eq 201) -and $rBook1.body -and ([string]$rBook1.body.id).StartsWith('bk_') -and ($rBook1.body.copies -eq 2) -and ($rBook1.body.availableCopies -eq 2) -and ($rBook1.body.title -eq 'Refactoring')
Add-Result 'S01' $ok '201 bk_* copies=2 avail=2' ("{0} id={1} avail={2}" -f $rBook1.status, $rBook1.body.id, $rBook1.body.availableCopies)
$book1 = [string]$rBook1.body.id
$probes.ids += $book1
Record-Extra 'POST/books' $rBook1.body @('id', 'title', 'copies', 'availableCopies')

# --- S02 会員登録 ---
$rAlice = Invoke-Api POST '/v1/members' @{ name = 'Alice' }
$ok = ($rAlice.status -eq 201) -and $rAlice.body -and ([string]$rAlice.body.id).StartsWith('mb_') -and ($rAlice.body.name -eq 'Alice')
Add-Result 'S02' $ok '201 mb_*' ("{0} id={1}" -f $rAlice.status, $rAlice.body.id)
$alice = [string]$rAlice.body.id
$probes.ids += $alice
function New-Member { param([string]$Name)
  $r = Invoke-Api POST '/v1/members' @{ name = $Name }
  if ($r.status -ne 201) { throw "member seed failed: $($r.raw)" }
  return [string]$r.body.id
}
function New-Book { param([string]$Title, [int]$Copies)
  $r = Invoke-Api POST '/v1/books' @{ title = $Title; copies = $Copies }
  if ($r.status -ne 201) { throw "book seed failed: $($r.raw)" }
  return [string]$r.body.id
}

# --- S03 検証違反 ---
$r = Invoke-Api POST '/v1/books' @{ title = ''; copies = 0 }
$t = Test-ErrorShape $r 400 'invalid_request'
Add-Result 'S03' $t.ok '400/invalid_request' $t.desc
if ($r.body -and $r.body.error) { $probes.errorMessages += [string]$r.body.error.message }

# --- S04 不在 ---
$r = Invoke-Api GET '/v1/books/bk_does_not_exist'
$t = Test-ErrorShape $r 404 'not_found'
Add-Result 'S04' $t.ok '404/not_found' $t.desc
if ($r.body -and $r.body.error) { $probes.errorMessages += [string]$r.body.error.message }

# --- S05 貸出成功(月またぎ暦日) ---
$rLoan1 = Invoke-Api POST '/v1/loans' @{ bookId = $book1; memberId = $alice; loanedAtUtc = '2026-01-31T10:00:00Z' }
$ok = ($rLoan1.status -eq 201) -and $rLoan1.body -and ([string]$rLoan1.body.id).StartsWith('ln_') -and ($rLoan1.body.dueDateUtc -eq '2026-02-14') -and ($rLoan1.body.status -eq 'active') -and (Test-Instant ([string]$rLoan1.body.loanedAtUtc) '2026-01-31T10:00:00Z')
Add-Result 'S05' $ok '201 due=2026-02-14 active' ("{0} due={1} status={2}" -f $rLoan1.status, $rLoan1.body.dueDateUtc, $rLoan1.body.status)
$loan1 = [string]$rLoan1.body.id
$probes.ids += $loan1
$probes.datetimeEcho += [string]$rLoan1.body.loanedAtUtc
Record-Extra 'POST/loans' $rLoan1.body @('id', 'bookId', 'memberId', 'loanedAtUtc', 'dueDateUtc', 'status')

# --- S06 在庫切れ(book1 copies=2: alice が1冊保有中) ---
$bob = New-Member 'Bob'; $carol = New-Member 'Carol'
$r2 = Invoke-Api POST '/v1/loans' @{ bookId = $book1; memberId = $bob; loanedAtUtc = '2026-01-31T11:00:00Z' }
$r3 = Invoke-Api POST '/v1/loans' @{ bookId = $book1; memberId = $carol; loanedAtUtc = '2026-01-31T12:00:00Z' }
$t = Test-ErrorShape $r3 409 'no_copies_available'
$ok = ($r2.status -eq 201) -and $t.ok
Add-Result 'S06' $ok '2nd=201, 3rd=409/no_copies_available' ("2nd={0} 3rd={1}" -f $r2.status, $t.desc)

# --- S07 上限(専用蔵書で Dave に4冊) ---
$dave = New-Member 'Dave'
$bigBook = New-Book 'Big Stock' 100
$bookX = New-Book 'X' 10; $bookY = New-Book 'Y' 10
$l1 = Invoke-Api POST '/v1/loans' @{ bookId = $bigBook; memberId = $dave; loanedAtUtc = '2026-03-01T00:00:00Z' }
$l2 = Invoke-Api POST '/v1/loans' @{ bookId = $bookX; memberId = $dave; loanedAtUtc = '2026-03-01T01:00:00Z' }
$l3 = Invoke-Api POST '/v1/loans' @{ bookId = $bookY; memberId = $dave; loanedAtUtc = '2026-03-01T02:00:00Z' }
$l4 = Invoke-Api POST '/v1/loans' @{ bookId = $bigBook; memberId = $dave; loanedAtUtc = '2026-03-01T03:00:00Z' }
$t = Test-ErrorShape $l4 409 'loan_limit_exceeded'
$ok = ($l1.status -eq 201) -and ($l2.status -eq 201) -and ($l3.status -eq 201) -and $t.ok
Add-Result 'S07' $ok '3rd=201, 4th=409/loan_limit_exceeded' ("1-3={0},{1},{2} 4th={3}" -f $l1.status, $l2.status, $l3.status, $t.desc)

# --- S08/S09/S10 日時受理ポリシー ---
$eve = New-Member 'Eve'
$r = Invoke-Api POST '/v1/loans' @{ bookId = $bigBook; memberId = $eve; loanedAtUtc = '2026-03-02T09:00:00+09:00' }
$t = Test-ErrorShape $r 400 'invalid_request'
Add-Result 'S08' $t.ok '400/invalid_request (+09:00)' $t.desc
$r = Invoke-Api POST '/v1/loans' @{ bookId = $bigBook; memberId = $eve; loanedAtUtc = '2026-03-02T09:00:00+00:00' }
$t = Test-ErrorShape $r 400 'invalid_request'
Add-Result 'S09' $t.ok '400/invalid_request (+00:00)' $t.desc
$r = Invoke-Api POST '/v1/loans' @{ bookId = $bigBook; memberId = $eve; loanedAtUtc = '2026-03-02T09:00:00.123Z' }
$ok = ($r.status -eq 201)
Add-Result 'S10' $ok '201 (.123Z accepted)' ("{0}" -f $r.status)
if ($ok) { $probes.datetimeEcho += [string]$r.body.loanedAtUtc }

# --- S11-S13 返却境界(Frank, due = 2026-06-15) ---
$frank = New-Member 'Frank'
$mkLoan = { param($MemberId, $At)
  $r = Invoke-Api POST '/v1/loans' @{ bookId = $bigBook; memberId = $MemberId; loanedAtUtc = $At }
  if ($r.status -ne 201) { throw "loan seed failed: $($r.raw)" }
  return $r
}
$f1 = & $mkLoan $frank '2026-06-01T09:00:00Z'   # due 2026-06-15
$r = Invoke-Api POST "/v1/loans/$($f1.body.id)/return" @{ returnedAtUtc = '2026-06-15T23:59:59Z' }
$ok = ($r.status -eq 200) -and ($r.body.fineAmount -eq 0) -and ($r.body.status -eq 'returned')
Add-Result 'S11' $ok '200 fine=0 (due-day 23:59:59Z)' ("{0} fine={1}" -f $r.status, $r.body.fineAmount)
Record-Extra 'POST/return' $r.body @('id', 'bookId', 'memberId', 'loanedAtUtc', 'dueDateUtc', 'status', 'returnedAtUtc', 'fineAmount')

$f2 = & $mkLoan $frank '2026-06-01T09:00:00Z'
$r = Invoke-Api POST "/v1/loans/$($f2.body.id)/return" @{ returnedAtUtc = '2026-06-16T00:00:00Z' }
$ok = ($r.status -eq 200) -and ($r.body.fineAmount -eq 100)
Add-Result 'S12' $ok '200 fine=100 (due+1 00:00:00Z)' ("{0} fine={1}" -f $r.status, $r.body.fineAmount)

$f3 = & $mkLoan $frank '2026-06-01T09:00:00Z'
$r = Invoke-Api POST "/v1/loans/$($f3.body.id)/return" @{ returnedAtUtc = '2026-06-18T10:30:00Z' }
$ok = ($r.status -eq 200) -and ($r.body.fineAmount -eq 300)
Add-Result 'S13' $ok '200 fine=300 (due+3)' ("{0} fine={1}" -f $r.status, $r.body.fineAmount)

# --- S14 再返却 ---
$r = Invoke-Api POST "/v1/loans/$($f3.body.id)/return" @{ returnedAtUtc = '2026-06-18T10:30:00Z' }
$t = Test-ErrorShape $r 409 'already_returned'
Add-Result 'S14' $t.ok '409/already_returned' $t.desc

# --- S15/S16 延滞ブロック(Grace) ---
$grace = New-Member 'Grace'
& $mkLoan $grace '2026-06-01T09:00:00Z' | Out-Null    # due 2026-06-15, 未返却
$r = Invoke-Api POST '/v1/loans' @{ bookId = $bookX; memberId = $grace; loanedAtUtc = '2026-06-16T08:00:00Z' }
$t = Test-ErrorShape $r 409 'member_overdue_blocked'
Add-Result 'S15' $t.ok '409/member_overdue_blocked (due+1)' $t.desc
$heidi = New-Member 'Heidi'
& $mkLoan $heidi '2026-06-01T09:00:00Z' | Out-Null    # due 2026-06-15
$r = Invoke-Api POST '/v1/loans' @{ bookId = $bookX; memberId = $heidi; loanedAtUtc = '2026-06-15T22:00:00Z' }
$ok = ($r.status -eq 201)
Add-Result 'S16' $ok '201 (due-day not blocked)' ("{0}" -f $r.status)

# --- S17 返却が瞬時で過去(同一暦日) ---
$ivan = New-Member 'Ivan'
$iv = & $mkLoan $ivan '2026-06-02T10:00:00Z'
$r = Invoke-Api POST "/v1/loans/$($iv.body.id)/return" @{ returnedAtUtc = '2026-06-02T09:00:00Z' }
$t = Test-ErrorShape $r 400 'invalid_request'
Add-Result 'S17' $t.ok '400/invalid_request (instant past)' $t.desc

# --- S18 一覧の順序と形(Judy: returned 1件 + active 2件) ---
$judy = New-Member 'Judy'
$j1 = & $mkLoan $judy '2026-05-01T09:00:00Z'
$j2 = & $mkLoan $judy '2026-05-01T09:00:00Z'   # 同一瞬時 → id 序数
$j3 = & $mkLoan $judy '2026-04-01T09:00:00Z'   # 最古
Invoke-Api POST "/v1/loans/$($j1.body.id)/return" @{ returnedAtUtc = '2026-05-02T09:00:00Z' } | Out-Null
$r = Invoke-Api GET "/v1/loans?memberId=$judy"
$ok = $false
if ($r.status -eq 200 -and $r.body -and $r.body.ContainsKey('items')) {
  $items = @($r.body.items)
  $expFirst = [string]$j3.body.id
  $pair = @([string]$j1.body.id, [string]$j2.body.id)
  [Array]::Sort($pair, [System.StringComparer]::Ordinal)   # id は序数比較(仕様 §2.6)
  $orderOk = ($items.Count -eq 3) -and ($items[0].id -eq $expFirst) -and ($items[1].id -eq $pair[0]) -and ($items[2].id -eq $pair[1])
  $shapeOk = $true
  foreach ($it in $items) {
    if ($it.status -eq 'active') {
      if ($it.ContainsKey('returnedAtUtc') -or $it.ContainsKey('fineAmount')) { $shapeOk = $false }
    } elseif ($it.status -eq 'returned') {
      if (-not ($it.ContainsKey('returnedAtUtc') -and $it.ContainsKey('fineAmount'))) { $shapeOk = $false }
    } else { $shapeOk = $false }
  }
  $bothStatuses = (@($items | Where-Object { $_.status -eq 'active' }).Count -eq 2) -and (@($items | Where-Object { $_.status -eq 'returned' }).Count -eq 1)
  $ok = $orderOk -and $shapeOk -and $bothStatuses
  Add-Result 'S18' $ok 'order(oldest,id-ordinal) + shape' ("count={0} order={1} shape={2}" -f $items.Count, $orderOk, $shapeOk)
} else {
  Add-Result 'S18' $false 'order + shape' ("status={0} items missing" -f $r.status)
}

# --- スナップショット(S19 用) ---
$snapBook1 = Invoke-Api GET "/v1/books/$book1"
$snapJudy = Invoke-Api GET "/v1/loans?memberId=$judy"

# --- S19 再起動永続性 ---
Write-Host "== restart for S19 =="
Stop-Api
Start-Api
$afterBook1 = Invoke-Api GET "/v1/books/$book1"
$afterJudy = Invoke-Api GET "/v1/loans?memberId=$judy"
$ok = ($afterBook1.status -eq 200) -and ($afterJudy.status -eq 200) -and
      ($afterBook1.body.availableCopies -eq $snapBook1.body.availableCopies) -and
      ($afterBook1.body.copies -eq $snapBook1.body.copies) -and
      (@($afterJudy.body.items).Count -eq @($snapJudy.body.items).Count)
$detail = ("avail {0}->{1} loans {2}->{3}" -f $snapBook1.body.availableCopies, $afterBook1.body.availableCopies, @($snapJudy.body.items).Count, @($afterJudy.body.items).Count)
if ($ok) {
  $beforeIds = @($snapJudy.body.items | ForEach-Object { [string]$_.id + ':' + [string]$_.status }) -join ','
  $afterIds = @($afterJudy.body.items | ForEach-Object { [string]$_.id + ':' + [string]$_.status }) -join ','
  $ok = ($beforeIds -eq $afterIds)
  $detail += (" ids-equal={0}" -f $ok)
}
Add-Result 'S19' $ok 'restart-survives (avail/loans/status equal)' $detail

# --- S20 NFR レイテンシ ---
Write-Host "== S20 latency =="
$latBook = New-Book 'Latency Bench' 100
$latMembers = @()
for ($i = 0; $i -lt 17; $i++) { $latMembers += New-Member ("bench-{0}" -f $i) }
$times = @()
$sw = New-Object System.Diagnostics.Stopwatch
for ($i = 0; $i -lt 50; $i++) {
  $m = $latMembers[$i % 17]
  $at = '2026-07-{0:d2}T00:00:{1:d2}Z' -f (1 + ($i % 28)), ($i % 60)
  $sw.Restart()
  $r = Invoke-Api POST '/v1/loans' @{ bookId = $latBook; memberId = $m; loanedAtUtc = $at }
  $sw.Stop()
  if ($r.status -eq 201) { $times += $sw.Elapsed.TotalMilliseconds }
  if (($i % 17) -eq 16) {
    # 上限回避: 各メンバー3件で頭打ちになるため、3周目ごとに新メンバー群へ
    $latMembers = @(); for ($k = 0; $k -lt 17; $k++) { $latMembers += New-Member ("bench-{0}-{1}" -f $i, $k) }
  }
}
$median = $null
if ($times.Count -ge 40) {
  $sorted = $times | Sort-Object
  $median = [math]::Round($sorted[[int][math]::Floor($sorted.Count / 2)], 1)
  $ok = ($median -lt 300)
} else { $ok = $false }
Add-Result 'S20' $ok 'median<300ms over 50 seq POST' ("median={0}ms n={1}" -f $median, $times.Count)
$probes.latencyMs = $median

Stop-Api

# --- 出力 ---
$passCount = @($script:results | Where-Object { $_.pass }).Count
$summary = @{
  factory = $FactoryDir
  oracle = 'forward-01 fixed oracle S01-S20'
  pass = $passCount
  total = $script:results.Count
  results = $script:results
  probes = $probes
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -Path $ResultFile -Encoding utf8
Write-Host ("== RESULT: {0}/{1} pass -> {2} ==" -f $passCount, $script:results.Count, $ResultFile)
