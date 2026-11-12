# Import-Ktool.ps1 — run in PowerShell after downloading Ktool_GitHub_Source.zip
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File .\Import-Ktool.ps1 -Archive "$env:USERPROFILE\Downloads\Ktool_GitHub_Source.zip"
param(
    [Parameter(Mandatory=$true)][string]$Archive
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Archive -PathType Leaf)) {
    throw "File ZIP not found: $Archive"
}
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git for Windows is required: https://git-scm.com/downloads/win'
}
$root = Join-Path ([IO.Path]::GetTempPath()) ("KtoolImport_" + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $root 'source'
$repo = Join-Path $root 'repository'
try {
    New-Item -ItemType Directory -Path $source -Force | Out-Null
    Expand-Archive -LiteralPath $Archive -DestinationPath $source -Force
    & git clone --branch main --single-branch 'https://github.com/huynhcongnghiem2004-beep/Ktool.git' $repo
    if ($LASTEXITCODE -ne 0) { throw 'Git clone failed. Check connection/authentication.' }
    $sourceFull = (Resolve-Path -LiteralPath $source).Path.TrimEnd('\') + '\'
    $files = Get-ChildItem -LiteralPath $source -File -Recurse
    foreach ($item in $files) {
        $relative = $item.FullName.Substring($sourceFull.Length)
        if ($relative -match '(^|[\\/])\.(git|vs)([\\/]|$)' -or
            $relative -match '(^|[\\/])(bin|obj|dist)([\\/]|$)') { continue }
        $target = Join-Path $repo $relative
        $parent = Split-Path -Parent $target
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
        Copy-Item -LiteralPath $item.FullName -Destination $target -Force
    }
    & git -C $repo add --all
    if ($LASTEXITCODE -ne 0) { throw 'git add failed.' }
    & git -C $repo diff --cached --quiet
    if ($LASTEXITCODE -eq 0) {
        Write-Host 'No new changes: Ktool source already uploaded.'
        return
    }
    & git -C $repo commit -m 'Import complete Ktool source for Revit 2024'
    if ($LASTEXITCODE -ne 0) { throw 'git commit failed: configure git user.name and user.email.' }
    & git -C $repo push origin main
    if ($LASTEXITCODE -ne 0) { throw 'git push failed: sign in to GitHub or resolve branch permissions.' }
    Write-Host 'SUCCESS: Ktool source uploaded to https://github.com/huynhcongnghiem2004-beep/Ktool'
} finally {
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}
