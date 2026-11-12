param([Parameter(Mandatory=$true)][string]$BuiltDll,[string]$BuiltPdb)
$ErrorActionPreference='Stop'
function Get-KToolFileHash([string]$LiteralPath){
 $stream=[IO.File]::OpenRead($LiteralPath)
 try{$sha=[Security.Cryptography.SHA256]::Create();try{[pscustomobject]@{Hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}}finally{$sha.Dispose()}}finally{$stream.Dispose()}
}
if(Get-Process Revit -ErrorAction SilentlyContinue){throw 'Close Revit before building and deploying KTool, or build with -p:SkipRevitDeploy=true for compile only.'}
$payload=Join-Path $PSScriptRoot 'dist\SDK_HD_Dim'
New-Item -ItemType Directory -Path $payload -Force|Out-Null
foreach($source in @($BuiltDll,$BuiltPdb)){if($source -and (Test-Path -LiteralPath $source)){Copy-Item -LiteralPath $source -Destination (Join-Path $payload (Split-Path -Leaf $source)) -Force}}
if(!(Test-Path -LiteralPath (Join-Path $payload 'KTool.DwgMerge.dll'))){throw 'Missing KTool.DwgMerge.dll dependency. Copy the complete release folder.'}
$files=foreach($file in Get-ChildItem -LiteralPath $payload -File){[pscustomobject]@{Name=$file.Name;SHA256=(Get-KToolFileHash -LiteralPath $file.FullName).Hash}}
[pscustomobject]@{Release='DIM3-R2-20261030';BuiltAt=(Get-Date -Format o);Files=@($files)}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'BuildInfo.json') -Encoding utf8
& (Join-Path $PSScriptRoot 'Install-Revit2024.ps1') -Destination (Join-Path $PSScriptRoot 'dist')
