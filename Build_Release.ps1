param([switch]$Install,[string]$Destination='')
$ErrorActionPreference='Stop'
function Get-KToolFileHash([string]$LiteralPath){
 $stream=[IO.File]::OpenRead($LiteralPath)
 try{$sha=[Security.Cryptography.SHA256]::Create();try{[pscustomobject]@{Hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}}finally{$sha.Dispose()}}finally{$stream.Dispose()}
}
$project=Join-Path $PSScriptRoot 'Dim.csproj'
dotnet build $project -c Release -p:SkipRevitDeploy=true
if($LASTEXITCODE -ne 0){throw 'Build failed.'}
$destinationDir=Join-Path $PSScriptRoot 'dist\SDK_HD_Dim'
New-Item -ItemType Directory -Path $destinationDir -Force | Out-Null
foreach($name in 'Dim.dll','Dim.pdb'){
 $source=Join-Path $PSScriptRoot ('bin\Release\'+$name)
 Copy-Item -LiteralPath $source -Destination (Join-Path $destinationDir $name) -Force
 if((Get-KToolFileHash -LiteralPath $source).Hash -ne (Get-KToolFileHash -LiteralPath (Join-Path $destinationDir $name)).Hash){throw "Hash mismatch: $name"}
}
$files=foreach($file in Get-ChildItem -LiteralPath $destinationDir -File){[pscustomobject]@{Name=$file.Name;SHA256=(Get-KToolFileHash -LiteralPath $file.FullName).Hash}}
[pscustomobject]@{Release='DIM3-R2-20261030';BuiltAt=(Get-Date -Format o);Logic='Stair Dim3 previously verified in Revit 2024.3; client installer corrected';Files=@($files)}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'BuildInfo.json') -Encoding utf8
if($Install -and !$Destination){$Destination=Join-Path $PSScriptRoot 'dist'}
if($Install){& (Join-Path $PSScriptRoot 'Install-Revit2024.ps1') -Destination $Destination}else{Write-Output 'Build complete. Run Install-Revit2024.ps1 after closing Revit to update registration.'}
