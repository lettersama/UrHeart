param([string]$ProjectRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))

# Windows-only integration tests against the shipped helper, using isolated fake
# desktops and disposable parent processes. Never sends a notification or touches
# player saves, the real desktop, Steam achievements, or persistent Windows tasks.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$root = Join-Path $ProjectRoot ('Temp/PostGameFarewellQA/exit-fix-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($root)
$scriptPath = Join-Path $root 'WindowsDelivery.ps1'
Copy-Item -LiteralPath (Join-Path $ProjectRoot 'Assets/Resources/PostGameFarewell/WindowsDelivery.ps1.txt') -Destination $scriptPath
$powershell = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
$utf8 = New-Object Text.UTF8Encoding($true)
$checks = New-Object 'Collections.Generic.List[string]'
$ownedProcesses = New-Object 'Collections.Generic.List[Diagnostics.Process]'
$observations = New-Object 'Collections.Generic.List[object]'

function Check([bool]$Pass, [string]$Label) {
    if (-not $Pass) { throw $Label }
    $checks.Add($Label)
}
function Write-Json([string]$Path, $Value) {
    [IO.File]::WriteAllText($Path, (ConvertTo-Json -InputObject $Value -Depth 12), $utf8)
}
function Wait-Until([scriptblock]$Predicate, [string]$Label, [int]$Seconds = 20) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while (-not (& $Predicate)) {
        if ($clock.Elapsed.TotalSeconds -gt $Seconds) { throw ('Timed out: ' + $Label) }
        Start-Sleep -Milliseconds 100
    }
}
function Start-Parent([string]$Name) {
    $signal = Join-Path $root ($Name + '-exit')
    $parentScript = Join-Path $root ($Name + '-parent.ps1')
    [IO.File]::WriteAllText($parentScript, "param([string]`$Signal)`nwhile (-not [IO.File]::Exists(`$Signal)) { Start-Sleep -Milliseconds 100 }", $utf8)
    $process = Start-Process -FilePath $powershell -ArgumentList @('-NoProfile','-NonInteractive','-ExecutionPolicy','RemoteSigned',
        '-WindowStyle','Hidden','-File',('"'+$parentScript+'"'),'-Signal',('"'+$signal+'"')) -WindowStyle Hidden -PassThru
    $ownedProcesses.Add($process)
    return [pscustomobject]@{ process=$process; signal=$signal; ticks=$process.StartTime.ToUniversalTime().Ticks.ToString() }
}
function Exit-Parent($Parent) {
    [IO.File]::WriteAllText($Parent.signal, 'exit')
    Wait-Until { $Parent.process.HasExited } 'parent exit'
}
function New-Fixture([string]$Name, $Parent, [int]$Delay = 0, [int]$Schema = 2) {
    $dir = Join-Path $root $Name
    [void][IO.Directory]::CreateDirectory($dir)
    $isolation = [Guid]::NewGuid().ToString('N')
    $request = [ordered]@{
        schemaVersion=$Schema; installationToken=[Guid]::NewGuid().ToString('N'); requestId=[Guid]::NewGuid().ToString('N')
        parentPid=$Parent.process.Id; parentStartTimeUtcTicks=$Parent.ticks; createdUtc=[DateTime]::UtcNow.ToString('O')
        delaySeconds=$Delay; language='en'; suggestedFileName='QA Letter.txt'; letterText=('Farewell QA: '+[char]0x4f60+[char]0x597d+' - goodbye')
        notification=@{title='QA';body='QA'};mode='test';disableToast=$true
        testDesktopDirectory=(Join-Path $dir 'desktop');testIsolationToken=$isolation
    }
    [IO.File]::WriteAllText((Join-Path $dir '.postgame-farewell-test'),$isolation,$utf8)
    [IO.File]::WriteAllText((Join-Path $dir 'installation-token.txt'),$request.installationToken,$utf8)
    Write-Json (Join-Path $dir 'request.json') $request
    return [pscustomobject]@{dir=$dir;request=$request;target=(Join-Path $request.testDesktopDirectory 'QA Letter.txt');parent=$Parent}
}
function Start-Helper($Fixture, [bool]$Broker = $true, [bool]$IsTest = $true) {
    $log = Join-Path $Fixture.dir ([Guid]::NewGuid().ToString('N'))
    $args = @('-NoLogo','-NoProfile','-NonInteractive','-Sta','-ExecutionPolicy','RemoteSigned','-WindowStyle','Hidden',
        '-File',('"'+$scriptPath+'"'),'-RequestPath',('"'+(Join-Path $Fixture.dir 'request.json')+'"'))
    if ($Broker) { $args += '-LaunchDetached' }
    if ($IsTest) { $args += '-TestMode' }
    $process = Start-Process -FilePath $powershell -ArgumentList $args -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput ($log+'.out') -RedirectStandardError ($log+'.err')
    $ownedProcesses.Add($process)
    return $process
}
function Wait-Worker($Fixture) {
    Wait-Until { @(Get-ChildItem -LiteralPath $Fixture.dir -Filter 'worker-*.json').Count -gt 0 } 'worker ready'
    $record = Get-ChildItem -LiteralPath $Fixture.dir -Filter 'worker-*.json' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    $worker = Get-Content -LiteralPath $record.FullName -Raw | ConvertFrom-Json
    $process = [Diagnostics.Process]::GetProcessById([int]$worker.helperPid)
    $ownedProcesses.Add($process)
    return $process
}
function Wait-Receipt($Fixture) {
    Wait-Until { Test-Path -LiteralPath (Join-Path $Fixture.dir 'receipt.json') } 'delivery receipt'
    return Get-Content -LiteralPath (Join-Path $Fixture.dir 'receipt.json') -Raw | ConvertFrom-Json
}
function Record($Fixture) {
    return [ordered]@{schemaVersion=2;requestId=$Fixture.request.requestId;installationToken=$Fixture.request.installationToken}
}

$failure = $null
try {
    $parent = Start-Parent 'normal'
    $normal = New-Fixture ('normal spaces '+[char]0x6d4b+[char]0x8bd5) $parent 2
    $launcher = Start-Helper $normal
    $worker = Wait-Worker $normal
    Wait-Until { $launcher.HasExited } 'short launcher exit' 12
    Check ($launcher.ExitCode -eq 0 -and -not $worker.HasExited -and -not $parent.process.HasExited) 'launcher exits while worker and game parent are alive'
    $native = Get-CimInstance Win32_Process -Filter ('ProcessId = ' + $worker.Id)
    $nativeParent = Get-CimInstance Win32_Process -Filter ('ProcessId = ' + $native.ParentProcessId)
    Check ($nativeParent.Name -eq 'WmiPrvSE.exe') 'waiting worker belongs to Windows WMI, not the game/launcher tree'
    Check ($worker.SessionId -eq [Diagnostics.Process]::GetCurrentProcess().SessionId) 'worker remains in the same interactive user session'
    $observations.Add(@{workerPid=$worker.Id;osParent=$nativeParent.Name;osParentPid=$native.ParentProcessId;launcherPid=$launcher.Id})
    Check (-not (Test-Path -LiteralPath $normal.target)) 'letter is not delivered before the original game exits'
    $duplicate = Start-Helper $normal
    Wait-Until { $duplicate.HasExited } 'duplicate launcher exit'
    Check (@(Get-ChildItem -LiteralPath $normal.dir -Filter 'worker-*.json').Count -eq 1) 'concurrent launcher does not create a second waiting worker'
    $exitAt = [DateTime]::UtcNow
    Exit-Parent $parent
    $receipt = Wait-Receipt $normal
    Check ($receipt.status -eq 'delivered' -and ([DateTime]::Parse($receipt.completedUtc).ToUniversalTime()-$exitAt).TotalSeconds -ge 1.8) 'delivery waits for the configured post-exit delay'
    Check ([IO.File]::ReadAllText($normal.target,$utf8) -ceq $normal.request.letterText) 'desktop letter preserves UTF-8 text'
    Wait-Until { $worker.HasExited } 'worker stops after delivery'
    $report=Get-Content -LiteralPath (Join-Path $normal.dir 'report.json') -Raw | ConvertFrom-Json
    Check (@($report.steamEnvironmentKeys).Count -eq 0 -and $report.notification.status -eq 'suppressed_test') 'worker has no Steam launch environment and test sends no notification'
    Check (@(Get-ChildItem -LiteralPath $normal.request.testDesktopDirectory -Filter '*.txt').Count -eq 1) 'exactly one TXT, no numbered duplicate'

    # A final receipt remains authoritative even if the user removes their TXT.
    Remove-Item -LiteralPath $normal.target
    $again = Start-Helper $normal
    Wait-Until { $again.HasExited } 'completed relaunch exit'
    Check (-not (Test-Path -LiteralPath $normal.target)) 'completed request never recreates a manually deleted letter'

    $parent = Start-Parent 'interrupted'
    $interrupted = New-Fixture 'interrupted' $parent 6
    $launcher = Start-Helper $interrupted
    $worker = Wait-Worker $interrupted
    Exit-Parent $parent
    Wait-Until { Test-Path -LiteralPath (Join-Path $interrupted.dir 'parent-exit.json') } 'persisted exit time'
    $oldExit=Get-Content -LiteralPath (Join-Path $interrupted.dir 'parent-exit.json') -Raw
    $worker.Kill(); $worker.WaitForExit()
    Check (-not (Test-Path -LiteralPath $interrupted.target)) 'killing worker during delay happens before desktop delivery'
    $launcher = Start-Helper $interrupted
    $receipt = Wait-Receipt $interrupted
    Check ($receipt.status -eq 'delivered' -and (Get-Content -LiteralPath (Join-Path $interrupted.dir 'parent-exit.json') -Raw) -ceq $oldExit) 'interrupted worker resumes the original request and deadline'

    $parent = Start-Parent 'dead'
    Exit-Parent $parent
    $legacy = New-Fixture 'legacy-v1-claim' $parent 0 1
    $claim=Record $legacy; $claim.schemaVersion=1; $claim.helperPid=1; $claim.claimedUtc=[DateTime]::UtcNow.ToString('O')
    Write-Json (Join-Path $legacy.dir 'claim.json') $claim
    $launcher = Start-Helper $legacy
    Check ((Wait-Receipt $legacy).status -eq 'delivered') 'legacy claimed-but-undelivered request can recover after Steam Stop'

    $existing = New-Fixture 'existing-file' $parent
    [void][IO.Directory]::CreateDirectory($existing.request.testDesktopDirectory)
    [IO.File]::WriteAllText($existing.target,'Do not change this file.',$utf8)
    $launcher = Start-Helper $existing
    Check ((Wait-Receipt $existing).status -eq 'skipped_existing' -and [IO.File]::ReadAllText($existing.target,$utf8) -ceq 'Do not change this file.') 'existing same-name file is preserved byte-for-byte'

    $prepared = New-Fixture 'prepared-publication' $parent
    [void][IO.Directory]::CreateDirectory($prepared.request.testDesktopDirectory)
    $tempFile=Join-Path $prepared.request.testDesktopDirectory ('.Live-'+$prepared.request.requestId+'-'+[Guid]::NewGuid().ToString('N')+'.tmp')
    [IO.File]::WriteAllText($tempFile,$prepared.request.letterText,$utf8)
    $publication=Record $prepared; $publication.temporaryPath=$tempFile; $publication.targetPath=$prepared.target
    Write-Json (Join-Path $prepared.dir 'publication.json') $publication
    $launcher = Start-Helper $prepared
    Check ((Wait-Receipt $prepared).status -eq 'delivered' -and -not (Test-Path -LiteralPath $tempFile)) 'recovery completes an interrupted atomic publication'

    $consumed = New-Fixture 'consumed-publication' $parent
    $publication=Record $consumed
    $publication.temporaryPath=Join-Path $consumed.request.testDesktopDirectory ('.Live-'+$consumed.request.requestId+'-'+[Guid]::NewGuid().ToString('N')+'.tmp')
    $publication.targetPath=$consumed.target
    Write-Json (Join-Path $consumed.dir 'publication.json') $publication
    $launcher = Start-Helper $consumed
    Check ((Wait-Receipt $consumed).status -eq 'already_published_no_retry' -and -not (Test-Path -LiteralPath $consumed.target)) 'crash after atomic move cannot cause redelivery even without a final receipt'

    $bad = New-Fixture 'bad-claim' $parent
    $claim=Record $bad; $claim.requestId=[Guid]::NewGuid().ToString('N')
    Write-Json (Join-Path $bad.dir 'claim.json') $claim
    $direct=Start-Helper $bad $false
    Wait-Until { $direct.HasExited } 'bad record rejection'
    Check ($direct.ExitCode -eq 1 -and -not (Test-Path -LiteralPath $bad.target)) 'mismatched claim fails without delivery'
    $unsafe = New-Fixture 'missing-test-switch' $parent
    $direct=Start-Helper $unsafe $false $false
    Wait-Until { $direct.HasExited } 'test mode rejection'
    Check ($direct.ExitCode -eq 1 -and -not (Test-Path -LiteralPath $unsafe.target)) 'test request cannot run without its explicit test gate'
} catch {
    $failure=$_.Exception.ToString()
} finally {
    foreach ($process in $ownedProcesses) {
        try { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(3000) | Out-Null } } catch { }
        $process.Dispose()
    }
    $result=[ordered]@{status=$(if($null -eq $failure){'PASS'}else{'FAIL'});passed=$checks.Count;checks=$checks.ToArray();error=$failure;observations=$observations.ToArray();directory=$root}
    Write-Json (Join-Path $root 'results.json') $result
    $result | ConvertTo-Json -Depth 8
}
if ($null -ne $failure) { exit 1 }
