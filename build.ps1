$ErrorActionPreference = 'Stop'

$framework = Join-Path $env:WINDIR 'Microsoft.NET'
$compiler = @(
    (Join-Path $framework 'Framework64\v4.0.30319\csc.exe')
    (Join-Path $framework 'Framework\v4.0.30319\csc.exe')
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (!$compiler) {
    throw 'The .NET Framework C# compiler was not found.'
}

$buildDir = Join-Path $PSScriptRoot 'build'
$executable = Join-Path $buildDir 'QuickBatchSnip.exe'
New-Item -ItemType Directory -Path $buildDir -Force | Out-Null

Push-Location (Join-Path $PSScriptRoot 'src')
try {
    & $compiler /nologo /warn:4 /target:winexe /platform:anycpu /optimize+ /codepage:65001 `
        /win32manifest:QuickBatchSnip.manifest /win32icon:QuickBatchSnip.ico "/out:$executable" `
        QuickBatchSnip.cs WindowsInput.cs StatusWindow.cs WindowsStartup.cs
    if ($LASTEXITCODE -ne 0) {
        throw "C# compilation failed with exit code $LASTEXITCODE."
    }
} finally {
    Pop-Location
}

Write-Output $executable
