# Compiles the V8 builder. Usage: .\build.ps1 [-Out <dir>] [extra .cs files]
param([string]$Out = "$PSScriptRoot\bin", [string[]]$Extra = @())
$sw = 'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force $Out | Out-Null
$src = @(Get-ChildItem "$PSScriptRoot\*.cs" | ForEach-Object FullName) + $Extra
& $csc /nologo /platform:x64 /lib:"$(Split-Path $csc)\WPF" /out:"$Out\V8Builder.exe" /r:"$sw\SolidWorks.Interop.sldworks.dll" /r:"$sw\SolidWorks.Interop.swconst.dll" /r:"$sw\SolidWorks.Interop.swmotionstudy.dll" /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll $src
if ($LASTEXITCODE -eq 0) {
    Copy-Item "$sw\SolidWorks.Interop.sldworks.dll", "$sw\SolidWorks.Interop.swconst.dll", "$sw\SolidWorks.Interop.swmotionstudy.dll" $Out -Force
    Write-Output "Built $Out\V8Builder.exe"
}
