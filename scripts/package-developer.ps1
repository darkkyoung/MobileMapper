param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'artifacts/MobileMapper'
$third = Join-Path $out 'third-party'
$notices = Join-Path $third 'notices'
$source = Join-Path $third 'source'
New-Item -ItemType Directory -Force $notices,$source | Out-Null
$lock = Get-Content "$root/dependencies.lock.json" -Raw | ConvertFrom-Json
function Download-Checked([string]$Url, [string]$Path, [string]$Hash, [string]$Algorithm = 'SHA256') {
    Invoke-WebRequest $Url -OutFile $Path
    if ((Get-FileHash $Path -Algorithm $Algorithm).Hash -ne $Hash) { Remove-Item $Path; throw "Dependency hash mismatch: $Url" }
}
Download-Checked $lock.scrcpy.url "$third/scrcpy-server-v5.0.1" $lock.scrcpy.sha256
Invoke-WebRequest "https://raw.githubusercontent.com/Genymobile/scrcpy/$($lock.scrcpy.commit)/LICENSE" -OutFile "$notices/scrcpy-LICENSE.txt"
# Complete exact upstream FFmpeg source plus the pinned vcpkg patches/build recipe.
Download-Checked 'https://github.com/ffmpeg/ffmpeg/archive/n9.0.2.tar.gz' "$source/ffmpeg-n9.0.2.tar.gz" '21bf3fbcdfd2f41ea6edeab40433fecfe362b09ecaa177a652471b54f9357011b4f74dab18245ab1c26f1a473b94205490baecb42289afd2effd603cd0b22b59' 'SHA512'
& git -C "$root/.tools/vcpkg" archive --format=tar.gz "--output=$source/vcpkg-source.tar.gz" $lock.vcpkg.commit
if ($LASTEXITCODE -ne 0) { throw 'Cannot package the exact vcpkg source/build patches.' }
Copy-Item "$root/build/native/vcpkg_installed/x64-windows/share/ffmpeg/copyright" "$notices/FFmpeg-copyright.txt"
Copy-Item "$root/dependencies.lock.json","$root/vcpkg.json","$root/global.json","$root/scripts/build.ps1","$root/scripts/package-developer.ps1" $source
$nativeEvidence = Get-Content "$root/build/native/Testing/Temporary/LastTest.log" -Raw
$nativeEvidence.Replace($root, '<CHECKOUT>').Replace($root.Replace('\','/'), '<CHECKOUT>') | Set-Content "$third/native-build-evidence.txt"
# Capture license/NOTICE files from the actual NuGet dependency closure, not unrelated cached packages.
$assets = Get-Content "$root/src/MobileMapper.App/obj/project.assets.json" -Raw | ConvertFrom-Json
$packageFolders = @($assets.packageFolders.PSObject.Properties.Name)
$inventory = @()
foreach ($entry in $assets.libraries.PSObject.Properties) {
    if ($entry.Value.type -ne 'package') { continue }
    $relative = $entry.Value.path
    $folder = $null
    foreach ($base in $packageFolders) { if (Test-Path (Join-Path $base $relative)) { $folder = Join-Path $base $relative; break } }
    if (!$folder) { throw "Missing restored package: $relative" }
    $destination = Join-Path $notices ($relative.Replace('/','-'))
    New-Item -ItemType Directory -Force $destination | Out-Null
    $files = @(Get-ChildItem $folder -Recurse -File | Where-Object { $_.Name -match '(?i)(license|notice|copying|copyright)' -or $_.Extension -eq '.nuspec' })
    foreach ($file in $files) {
        $target = Join-Path $destination ([IO.Path]::GetRelativePath($folder, $file.FullName))
        New-Item -ItemType Directory -Force (Split-Path $target -Parent) | Out-Null
        Copy-Item $file.FullName $target
    }
    $nupkg = Get-ChildItem $folder -Filter '*.nupkg' | Select-Object -First 1
    $inventory += [ordered]@{ package = $entry.Name; nugetContentHash = $entry.Value.sha512; archiveSha256 = if ($nupkg) {(Get-FileHash $nupkg.FullName).Hash} else {$null} }
}
# Runtime packs may be download dependencies rather than library entries.
foreach ($base in $packageFolders) {
    $runtimeRoot = Join-Path $base 'microsoft.netcore.app.runtime.win-x64'
    if (Test-Path $runtimeRoot) {
        foreach ($runtime in Get-ChildItem $runtimeRoot -Directory) {
            $dest = Join-Path $notices "dotnet-runtime-$($runtime.Name)"
            New-Item -ItemType Directory -Force $dest | Out-Null
            Get-ChildItem $runtime.FullName -File | Where-Object { $_.Name -match '(?i)(license|notice)' } | Copy-Item -Destination $dest
        }
    }
}
$inventory | ConvertTo-Json -Depth 5 | Set-Content "$third/nuget-manifest.json"
Copy-Item "$root/docs/THIRD_PARTY.md","$root/docs/PHASE1_DEVICE_TEST.md","$root/docs/PHASE1_IMPLEMENTATION.md" $third
@'
MobileMapper developer build — Windows 11 x64. Physical-device acceptance pending.
Read third-party/PHASE1_DEVICE_TEST.md before running MobileMapper.App.exe.
Install Microsoft's current Visual C++ x64 Redistributable if the native DLL cannot load.
ADB is NOT bundled. Download official Android Platform-Tools and select adb.exe in the app.
This software uses libraries from the FFmpeg project under LGPL-2.1-or-later.
Exact corresponding FFmpeg source and vcpkg patches/recipes are in third-party/source.
Rebuild with the pinned vcpkg source and the included vcpkg.json; upstream patches are in ports/ffmpeg.
The shared FFmpeg DLLs may be replaced; this package imposes no restriction on debugging modifications to LGPL libraries.
Microsoft and other third-party components remain governed by their accompanying license terms.
Use and redistribution of those components must comply with the packaged applicable terms.
The developer package is not a signed installer or an approved public product release.
'@ | Set-Content "$out/READ-ME-FIRST.txt"
# Hash every shipped payload (manifest itself excluded). No ADB credentials or machine paths.
Get-ChildItem $out -Recurse -File | ForEach-Object {
    [ordered]@{ path = [IO.Path]::GetRelativePath($out, $_.FullName).Replace('\','/'); sha256 = (Get-FileHash $_.FullName).Hash.ToLowerInvariant(); bytes = $_.Length }
} | ConvertTo-Json -Depth 4 | Set-Content "$out/artifact-manifest.json"
