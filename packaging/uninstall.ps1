<#
.SYNOPSIS
Removes CodePad for the current user. It keeps your experiments and settings unless you add -PurgeData.

.PARAMETER PurgeData
Also deletes %LOCALAPPDATA%\CodePad (the database and the settings). That cannot be undone.
#>
param([switch]$PurgeData)

$ErrorActionPreference = 'Stop'

$root = if ($env:CODEPAD_INSTALL_DIR) { $env:CODEPAD_INSTALL_DIR } else { Join-Path $env:LOCALAPPDATA 'Programs\CodePad' }
$app = Join-Path $root 'app'
foreach ($path in @($app, (Join-Path $root 'app.new'), (Join-Path $root 'app.old'))) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}
if ((Test-Path -LiteralPath $root) -and -not (Get-ChildItem -LiteralPath $root -Force)) { Remove-Item -LiteralPath $root -Force }

if (-not $env:CODEPAD_SKIP_SHORTCUT) {
    $shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\CodePad.lnk'
    if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut -Force }
}

if (-not $env:CODEPAD_SKIP_PATH) {
    $current = [Environment]::GetEnvironmentVariable('Path', 'User')
    $entries = @($current -split ';' | Where-Object { $_ -and $_ -ne $app })
    [Environment]::SetEnvironmentVariable('Path', ($entries -join ';'), 'User')
}

if ($PurgeData) {
    $data = Join-Path $env:LOCALAPPDATA 'CodePad'
    if (Test-Path -LiteralPath $data) { Remove-Item -LiteralPath $data -Recurse -Force }
    Write-Output 'CodePad and your data are removed.'
}
else {
    Write-Output 'CodePad is removed. Your data is kept in %LOCALAPPDATA%\CodePad.'
}
