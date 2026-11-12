# Import-Ktool.ps1
# Clone Ktool and import source from a local directory or a cleaned ZIP archive.
# Usage: .\Import-Ktool.ps1 -SourceDirectory 'D:\SDK_HD\Dim\Dim'
# Or:    .\Import-Ktool.ps1 -Archive 'C:\Downloads\Ktool_GitHub_Source.zip'
[CmdletBinding(DefaultParameterSetName='Directory')]
param(
    [Parameter(Mandatory=$true,ParameterSetName='Directory')]
    [string]$SourceDirectory,
    [Parameter(Mandatory=$true,ParameterSetName='Archive')]
    [string]$Archive
)
$ErrorActionPreference='Stop'
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Install Git for Windows first: https://git-scm.com/downloads/win'
}
$work=Join-Path ([IO.Path]::GetTempPath()) ('KtoolImport_'+[Guid]::NewGuid().ToString('N'))
$repo=Join-Path $work 'Ktool'
try {
    if ($PSCmdlet.ParameterSetName -eq 'Archive') {
        if (-not (Test-Path -LiteralPath $Archive -PathType Leaf)) {throw "Archive not found: $Archive"}
        $source=Join-Path $work 'source'
        New-Item -ItemType Directory -Force -Path $source | Out-Null
        Expand-Archive -LiteralPath $Archive -DestinationPath $source -Force
    } else {
        if (-not (Test-Path -LiteralPath $SourceDirectory -PathType Container)) {
            throw "Source directory not found: $SourceDirectory"
        }
        $source=(Resolve-Path -LiteralPath $SourceDirectory).Path
    }
    & git clone --branch main --single-branch 'https://github.com/huynhcongnghiem2004-beep/Ktool.git' $repo
    if ($LASTEXITCODE -ne 0) {throw 'git clone failed'}
    $sourcePrefix=(Resolve-Path -LiteralPath $source).Path.TrimEnd('\')+'\'
    $count=0
    foreach ($item in Get-ChildItem -LiteralPath $source -File -Recurse -Force) {
        $relative=$item.FullName.Substring($sourcePrefix.Length)
        # Exclude local machine state, generated output and typical secrets.
        if ($relative -match '(^|[\\/])(\.git|\.vs|bin|obj|dist|packages|node_modules|TestResults)([\\/]|$)' -or
            $relative -match '(^|[\\/])\.env($|\.)' -or
            $relative -match '\.(pfx|p12|snk|user|suo|cache|pdb|log)$' -or
            $relative -match '(^|[\\/])(appsettings\.Production\.json|secrets\.json)$') {continue}
        if ($item.Length -gt 10MB) {
            Write-Warning ('Skipped large file (>10MB): '+$relative)
            continue
        }
        $destination=Join-Path $repo $relative
        $parent=Split-Path -Parent $destination
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
        Copy-Item -LiteralPath $item.FullName -Destination $destination -Force
        $count++
    }
    Write-Host ("Prepared {0} source files. Review git status before upload." -f $count)
    & git -C $repo status --short
    if ($LASTEXITCODE -ne 0) {throw 'git status failed'}
    & git -C $repo add --all
    if ($LASTEXITCODE -ne 0) {throw 'git add failed'}
    & git -C $repo diff --cached --quiet
    if ($LASTEXITCODE -eq 0) {
        Write-Host 'Ktool is already up to date; no files to push.'
    } else {
        & git -C $repo commit -m 'Import Ktool Revit 2024 project source'
        if ($LASTEXITCODE -ne 0) {
            throw 'git commit failed. Configure git user.name and git user.email.'
        }
        & git -C $repo push origin main
        if ($LASTEXITCODE -ne 0) {throw 'git push failed. Verify GitHub authentication.'}
        Write-Host 'SUCCESS: Ktool source uploaded to https://github.com/huynhcongnghiem2004-beep/Ktool'
    }
} finally {
    if (Test-Path -LiteralPath $work) {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}
