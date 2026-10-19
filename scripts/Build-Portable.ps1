param(
    [string]$DotNet = 'dotnet',
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$OutputDirectory = 'artifacts'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $runtimePath = Join-Path $projectRoot 'assets/runtime/python.zip'
    $expected = '97DAE5274CC54867065E8D5A3226E48C35017ED332A0FDB0E27D5B5821961297'
    if (-not (Test-Path -LiteralPath $runtimePath)) {
        $staging = "$runtimePath.download"
        try {
            Invoke-WebRequest 'https://www.python.org/ftp/python/3.13.16/python-3.13.16-embed-amd64.zip' -OutFile $staging
            if ((Get-FileHash -LiteralPath $staging -Algorithm SHA256).Hash -ne $expected) { throw 'Official Python archive did not match the pinned hash.' }
            Move-Item -LiteralPath $staging -Destination $runtimePath
        } finally { if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging } }
    }
    if ((Get-FileHash -LiteralPath $runtimePath -Algorithm SHA256).Hash -ne $expected) { throw 'Embedded Python archive integrity check failed.' }
    & $DotNet restore ZeroBreach.sln --locked-mode --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Locked dependency restore failed.' }
    & $DotNet test tests/ZeroBreach.Tests -c Release --no-restore --nologo --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    $publishArgs = @('publish','src/ZeroBreach.App','-c','Release','--no-restore','-o',$OutputDirectory,'--nologo','--verbosity','minimal')
    if ($Version) { $publishArgs += "-p:Version=$Version" }
    & $DotNet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed.' }
    $outputRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
    $exe = Join-Path $outputRoot 'ZeroBreach.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw 'Expected single-file executable was not produced.' }
    $files = Get-ChildItem -LiteralPath $outputRoot -File
    if ($files.Count -ne 1 -or $files[0].Name -ne 'ZeroBreach.exe') { throw 'Unexpected companion files in artifacts; inspect the output before distribution.' }
    Get-Item -LiteralPath $exe | Select-Object FullName,Length
    Get-FileHash -LiteralPath $exe -Algorithm SHA256
} finally { Pop-Location }
