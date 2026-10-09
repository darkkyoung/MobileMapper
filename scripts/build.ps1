param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    function Run([string]$Program, [string[]]$Arguments) {
        & $Program @Arguments
        if ($LASTEXITCODE -ne 0) { throw "$Program failed ($LASTEXITCODE)" }
    }
    $vcpkgPath = Join-Path $root '.tools/vcpkg'
    if (!(Test-Path "$vcpkgPath/.git")) { Run git @('clone','https://github.com/microsoft/vcpkg.git',$vcpkgPath) }
    Run git @('-C',$vcpkgPath,'checkout','0699a19d0c6386247ce50d4dedbb8217d484d536')
    Run "$vcpkgPath/bootstrap-vcpkg.bat" @('-disableMetrics')
    Run cmake @('-S','.', '-B','build/native','-A','x64',"-DCMAKE_TOOLCHAIN_FILE=$vcpkgPath/scripts/buildsystems/vcpkg.cmake",'-DVCPKG_TARGET_TRIPLET=x64-windows')
    Run cmake @('--build','build/native','--config',$Configuration,'--parallel')
    Run ctest @('--test-dir','build/native','-C',$Configuration,'--output-on-failure')
    Run dotnet @('test','tests/MobileMapper.Tests/MobileMapper.Tests.csproj','-c',$Configuration,'--logger','trx','--results-directory','artifacts/test-results')
    Run dotnet @('publish','src/MobileMapper.App/MobileMapper.App.csproj','-c',$Configuration,'-r','win-x64','--self-contained','true','-o','artifacts/MobileMapper')
    Copy-Item "build/native/src/MobileMapper.Media.Native/$Configuration/MobileMapperMedia.dll" artifacts/MobileMapper/
    $dllDir = if ($Configuration -eq 'Debug') { 'debug/bin' } else { 'bin' }
    Copy-Item "build/native/vcpkg_installed/x64-windows/$dllDir/*.dll" artifacts/MobileMapper/
} finally { Pop-Location }
