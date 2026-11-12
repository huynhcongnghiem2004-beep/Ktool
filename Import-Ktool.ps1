# Upload an existing Windows source directory to Ktool on GitHub.
# Usage:
# powershell -NoProfile -ExecutionPolicy Bypass -File .\Import-Ktool.ps1 -SourceDirectory "D:\SDK_HD\Dim\Dim"
# You can also use -Archive "C:\Downloads\Ktool_GitHub_Source.zip".
param(
    [string]$SourceDirectory = "D:\SDK_HD\Dim\Dim",
    [string]$Archive
)
$ErrorActionPreference = 'Stop'
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "Git for Windows not found. Install from https://git-scm.com/downloads/win"
}
$temp = Join-Path ([IO.Path]::GetTempPath()) ("KtoolImport_" + [Guid]::NewGuid().ToString('N'))
$src = $SourceDirectory
$staged = Join-Path $temp 'repository'
try {
    if ($Archive) {
        if (-not (Test-Path -LiteralPath $Archive -PathType Leaf)) {
            throw "Archive not found: $Archive"
        }
        $src = Join-Path $temp 'source'
        New-Item -ItemType Directory -Path $src -Force | Out-Null
        Expand-Archive -LiteralPath $Archive -DestinationPath $src -Force
    }
    if (-not (Test-Path -LiteralPath $src -PathType Container)) {
        throw "Source directory not found: $src"
    }
    New-Item -ItemType Directory -Path $temp -Force | Out-Null
    & git clone --branch main --single-branch 'https://github.com/huynhcongnghiem2004-beep/Ktool.git' $staged
    if ($LASTEXITCODE -ne 0) { throw "Cannot clone Ktool" }
    $prefix = (Resolve-Path -LiteralPath $src).Path.TrimEnd('\') + '\'
    $count = 0
    foreach ($item in (Get-ChildItem -LiteralPath $src -Recurse -File -Force)) {
        $rel = $item.FullName.Substring($prefix.Length)
        if ($rel -match '(^|[\\/])(\.git|\.vs|bin|obj|dist|packages|TestResults)([\\/]|$)' -or
            $rel -match '(?i)(\.user$|\.suo$|\.vsidx$|\.pdb$|\.cache$|\.dll$|\.exe$|\.zip$|\.db$|\.sqlite$|\.local\.props$|\.addin$)' -or
            $rel -match '(?i)(^|[\\/])(\.env|id_rsa|id_ed25519|secrets?\.json)$') { continue }
        if ($item.Length -gt 20MB) { throw "Large file requires review: $rel" }
        $dst = Join-Path $staged $rel
        New-Item -ItemType Directory -Path (Split-Path -Parent $dst) -Force | Out-Null
        Copy-Item -LiteralPath $item.FullName -Destination $dst -Force
        $count++
    }
    if ($count -eq 0) { throw "No source files found. Check path." }
    & git -C $staged add --all
    if ($LASTEXITCODE -ne 0) { throw "git add failed" }
    & git -C $staged diff --cached --quiet
    if ($LASTEXITCODE -eq 0) {
        Write-Host "No changes to upload. Ktool already matches local source."
        return
    }
    if ($LASTEXITCODE -ne 1) { throw "Cannot inspect staged changes" }
    & git -C $staged diff --cached --stat
    Write-Host "Review the above files. Repository is PUBLIC."
    $answer = Read-Host "Upload these files to Ktool/main? Type YES"
    if ($answer -cne 'YES') { throw "Upload cancelled by user" }
    & git -C $staged commit -m 'Import Ktool Revit 2024 add-in source'
    if ($LASTEXITCODE -ne 0) { throw "Commit failed. Configure git user.name and user.email." }
    & git -C $staged push origin main
    if ($LASTEXITCODE -ne 0) { throw "Push failed. Sign in to GitHub and retry." }
    Write-Host "SUCCESS: https://github.com/huynhcongnghiem2004-beep/Ktool"
}
finally {
    if (Test-Path -LiteralPath $temp) {
        Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
