<#
.SYNOPSIS
Installs CodePad for the current user from a release archive. It needs no administrator rights and no .NET.

.DESCRIPTION
The archive goes to %LOCALAPPDATA%\Programs\CodePad\app. A Start menu shortcut is made, and the folder is added to the PATH of your user so that
the command "codepad" works in new terminals. Running the script again updates an existing installation: the new files are unpacked next to the old
ones and replace them in one step. Your data is not in that folder and is never touched.

.PARAMETER Archive
The release archive, codepad-<version>-win-x64.zip.

.PARAMETER Sha256
The expected SHA-256 digest of the archive. When a SHA256SUMS file stands next to the archive, the script reads the digest from it.

Variables for tests: CODEPAD_INSTALL_DIR, CODEPAD_SKIP_SHORTCUT, CODEPAD_SKIP_PATH.
#>
param(
    [Parameter(Mandatory = $true)][string]$Archive,
    [string]$Sha256
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Archive -PathType Leaf)) { throw 'The archive does not exist.' }
$archivePath = (Resolve-Path -LiteralPath $Archive).Path

if (-not $Sha256) {
    $sums = Join-Path (Split-Path -Parent $archivePath) 'SHA256SUMS'
    if (Test-Path -LiteralPath $sums) {
        $name = Split-Path -Leaf $archivePath
        $line = Get-Content -LiteralPath $sums | Where-Object { $_ -like "* *$name" -or $_ -like "*  $name" } | Select-Object -First 1
        if ($line) { $Sha256 = ($line -split '\s+')[0] }
    }
}
if ($Sha256) {
    $actual = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $Sha256.ToLowerInvariant()) { throw 'The SHA-256 digest of the archive does not match. Nothing was installed.' }
}

$root = if ($env:CODEPAD_INSTALL_DIR) { $env:CODEPAD_INSTALL_DIR } else { Join-Path $env:LOCALAPPDATA 'Programs\CodePad' }
$app = Join-Path $root 'app'
$stage = Join-Path $root 'app.new'
$old = Join-Path $root 'app.old'
New-Item -ItemType Directory -Force -Path $root | Out-Null
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }

try {
    Expand-Archive -LiteralPath $archivePath -DestinationPath $stage -Force
    if (-not (Test-Path -LiteralPath (Join-Path $stage 'Codepad.Desktop.exe'))) { throw 'The archive has no Codepad.Desktop.exe program. Nothing was installed.' }
    if (-not (Test-Path -LiteralPath (Join-Path $stage 'codepad.exe'))) { throw 'The archive has no codepad.exe command. Nothing was installed.' }

    if (Test-Path -LiteralPath $app) {
        if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Recurse -Force }
        Move-Item -LiteralPath $app -Destination $old
    }
    Move-Item -LiteralPath $stage -Destination $app
    if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Recurse -Force }
}
finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}

if (-not $env:CODEPAD_SKIP_SHORTCUT) {
    $programs = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
    New-Item -ItemType Directory -Force -Path $programs | Out-Null
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut((Join-Path $programs 'CodePad.lnk'))
    $shortcut.TargetPath = Join-Path $app 'Codepad.Desktop.exe'
    $shortcut.WorkingDirectory = $app
    $shortcut.Save()
}

if (-not $env:CODEPAD_SKIP_PATH) {
    $current = [Environment]::GetEnvironmentVariable('Path', 'User')
    $entries = @($current -split ';' | Where-Object { $_ })
    if ($entries -notcontains $app) {
        [Environment]::SetEnvironmentVariable('Path', (($entries + $app) -join ';'), 'User')
    }
}

Write-Output "CodePad is installed in $app"
Write-Output 'Your data stays in %LOCALAPPDATA%\CodePad. Remove CodePad with uninstall.ps1.'
