[CmdletBinding()]
param([string]$Destination='D:\SDK_HD\KTool_Revit2024',[switch]$ValidateOnly)
$ErrorActionPreference='Stop'
function Get-KToolFileHash([string]$LiteralPath){
 $stream=[IO.File]::OpenRead($LiteralPath)
 try{$sha=[Security.Cryptography.SHA256]::Create();try{[pscustomobject]@{Hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}}finally{$sha.Dispose()}}finally{$stream.Dispose()}
}
$payload=Join-Path $PSScriptRoot 'dist\SDK_HD_Dim'
$info=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'BuildInfo.json') -Raw|ConvertFrom-Json
foreach($item in $info.Files){$file=Join-Path $payload $item.Name;if(!(Test-Path -LiteralPath $file) -or (Get-KToolFileHash -LiteralPath $file).Hash -ne $item.SHA256){throw "Package hash mismatch: $($item.Name)"}}
$project=Join-Path $PSScriptRoot 'Dim.csproj'
if(Test-Path -LiteralPath $project){foreach($name in 'StairDeploymentCommand.cs','StairPlanAnnotations.cs','StairThreeLayerDimensions.cs'){if(!(Test-Path -LiteralPath (Join-Path $PSScriptRoot $name))){throw "Missing Dim3 source: $name"}}}
$destinationFull=[IO.Path]::GetFullPath($Destination).TrimEnd('\')
if($destinationFull -notmatch '^[D-Zd-z]:\\SDK_HD\\' -or $destinationFull.Contains('..')){throw 'Choose a subfolder on D:\SDK_HD (or another non-C drive under SDK_HD).'}
$addinDir=Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2024'
$manifest=Join-Path $addinDir 'Dim.addin'
$id='3f8a2c64-9b1e-4d57-a6c3-7e0d5b12f948'
foreach($dir in @($addinDir,(Join-Path $env:ProgramData 'Autodesk\Revit\Addins\2024'))){
 foreach($file in Get-ChildItem -LiteralPath $dir -Filter *.addin -ErrorAction SilentlyContinue){
  [xml]$other=Get-Content -LiteralPath $file.FullName
  foreach($entry in $other.RevitAddIns.AddIn){if(($entry.AddInId -eq $id -or $entry.FullClassName -eq 'SDK_HD.Dim.App') -and $file.FullName -ine $manifest){throw "Duplicate KTool registration: $($file.FullName). Resolve it before installation."}}
 }
}
Write-Output "Package verified: $($info.Release)"
if($ValidateOnly){Write-Output 'Validation only: no installation changes.';return}
if(Get-Process Revit -ErrorAction SilentlyContinue){throw 'Close Revit before installing.'}
$backup=Join-Path (([IO.Path]::GetPathRoot($destinationFull))+'SDK_HD\DeploymentBackups') ('KTool_'+(Get-Date -Format yyyyMMdd_HHmmssfff))
New-Item -ItemType Directory -Path $backup -Force|Out-Null
if(Test-Path -LiteralPath $manifest){Copy-Item -LiteralPath $manifest -Destination (Join-Path $backup 'Dim.addin.backup')}
$runtime=Join-Path $destinationFull 'SDK_HD_Dim'
New-Item -ItemType Directory -Path $runtime -Force|Out-Null
foreach($item in $info.Files){$target=Join-Path $runtime $item.Name;if(Test-Path -LiteralPath $target){Copy-Item -LiteralPath $target -Destination $backup};if([IO.Path]::GetFullPath((Join-Path $payload $item.Name)) -ine [IO.Path]::GetFullPath($target)){Copy-Item -LiteralPath (Join-Path $payload $item.Name) -Destination $target -Force};if((Get-KToolFileHash -LiteralPath $target).Hash -ne $item.SHA256){throw "Installed hash mismatch: $($item.Name)"}}
$assembly=[Security.SecurityElement]::Escape((Join-Path $runtime 'Dim.dll'))
$manifestText="<?xml version=`"1.0`" encoding=`"utf-8`"?><RevitAddIns><AddIn Type=`"Application`"><Name>KTool</Name><Assembly>$assembly</Assembly><AddInId>$id</AddInId><FullClassName>SDK_HD.Dim.App</FullClassName><VendorId>SDKHD</VendorId><VendorDescription>KTool Revit 2024 Dim3</VendorDescription></AddIn></RevitAddIns>"
New-Item -ItemType Directory -Path $addinDir -Force|Out-Null
[IO.File]::WriteAllText($manifest,$manifestText,[Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'BuildInfo.json') -Destination (Join-Path $destinationFull 'BuildInfo.json') -Force
Write-Output "Installed: $runtime\Dim.dll"
Write-Output "Backup: $backup"
Write-Output 'Start Revit 2024. KTool > Trien khai cau thang bo.'
