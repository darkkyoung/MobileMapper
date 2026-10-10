# Third-party review — Phase 0 baseline and Phase 1 artifacts

Reviewed 2026-10-08. This is an engineering inventory based on actual upstream
license texts and the inspected artifact below. **No third-party production
binary or source is redistributed by this commit.** A source project's license
does not certify every prebuilt package carrying its name. Exact build manifests,
transitive notices and corresponding-source packages are release requirements.

## Selected components and evaluated alternatives

| Component / purpose | Verified license / source | Redistribution and linking implications | Notices / status |
| --- | --- | --- | --- |
| scrcpy server 5.0.1 — Android capture/control | Apache-2.0 in pinned LICENSE [L1] | Source/object redistribution permitted subject to terms; no general obligation to publish MobileMapper source. Static versus socket/dynamic integration does not create copyleft. Modified files need change notices. | Include license, copyright and any applicable upstream NOTICE; preserve attribution. Prefer unchanged server. Artifact SHA/provenance to be pinned before use. |
| AOSP ADB core — wireless transport/auth | Apache-2.0 in AOSP NOTICE and source/build declarations [L2] | Core license allows redistribution; **ADB dependency closure has other licenses**. Process separation does not remove binary's own obligations. Static LGPL dependencies could require ADB relinkable objects/source even though C# app is separate. | Include component notices and any required source/relink materials. Core source inspection is not blanket prebuilt clearance. |
| Google SDK / Platform-Tools prebuilt 37.0.1 | SDK agreement §3.4 limits redistribution; §3.5 says open-source components are governed by their open-source licenses [L5]. ZIP includes aggregated NOTICE. | Do not label the entire SDK Apache-2.0 or claim all rebundling is forbidden. Determine licenses of the exact selected executable/DLLs and dependencies. | **Pending artifact-to-source/license mapping**; hashes below. No installer bundle authorized by this report alone. |
| FFmpeg `libavcodec` / `libavutil` — decode | Normally LGPL-2.1-or-later; optional GPL/nonfree/version3 code changes the result [L6,L7] | Choose shared LGPL-only build. Provide exact corresponding library source including changes/build scripts and allow replacement/relinking/debugging modifications. Dynamic linking does not remove source/notice obligations. Static use needs additional relinkable application material; not selected. | License text, prominent attribution, exact source distribution link/package, configuration, patches and hashes. Release build not produced here. |
| .NET runtime / C# libraries | dotnet/runtime source MIT [L8]; prebuilt runtime includes third-party notices | MIT permits static/dynamic redistribution with notices, no general app-source publication requirement. Preserve the actual runtime distribution's notices and terms. | Ship runtime license and THIRD-PARTY-NOTICES from selected self-contained runtime pack. |
| Windows App SDK / WinUI 3 | Repositories MIT [L9,L10]; binary SDK payload must be checked separately | Source license permits integration; use supported self-contained redistribution and actual NuGet/runtime notices. Windows/system APIs and other included Microsoft payloads are not automatically MIT. | Capture exact package licenses, redistributable files and transitive notices before packaging. |
| D3D11 / DXGI, optional Media Foundation | Windows platform APIs, not a third-party open-source codec library | Use OS-provided system DLLs. Do not ship copies of arbitrary Windows DLLs. Platform API use does not require publishing app source. | Native bridge's MSVC CRT runtime must follow the actual Visual Studio redistribution terms; select app-local permitted CRT files at build time. |
| Inno Setup — future installer | Inno Setup License, permissive custom text [L11] | Allows commercial/noncommercial use and redistribution under its conditions; source notices retained, binary notices/URLs retained, modifications marked. Not GPL. | Preserve installer/component notices. Check the pinned installer package's additional component licenses. No installer created now. |
| Qt — evaluated, not selected | Qt licensing is module-specific: LGPLv3/GPL/commercial alternatives, some modules GPL-only [L3] | Dynamic LGPL use can avoid app-source publication if all conditions are met; corresponding Qt source, replacement/relinking rights and installation information remain. Static linking adds material obligations; “static always forces entire app open” is not a sufficient analysis. | No Qt bundle planned. If adopted, audit each module and commercial/open-source option. |
| Avalonia core — evaluated, not selected | MIT in upstream licence.md [L4] | Core redistribution allowed with notices, no general app-source publication obligation. Commercial add-ons and Skia/transitive native components are separate. | No Avalonia or add-on bundle planned. |
| WPF — evaluated, not selected | MIT in dotnet/wpf [L12] | Same MIT notice principle; runtime dependency closure still needs its notices. | No WPF-specific dependency introduced. |
| Python / local FFmpeg + libx264 — experiment tools | Python standard library used; existing system FFmpeg reports `--enable-gpl` and libx264 | Developer execution only. The experiment's use of libx264 does not make it an intended product dependency. Generated fixture media stays in memory. | No interpreter/FFmpeg/libx264 binary, source or media fixture included in Git or the product plan. |

Rust/Tauri/Electron were architectural alternatives only, not adopted dependencies;
no blanket license claim is made for an unselected ecosystem. The planned native
bridge is original MobileMapper code, not a third-party wrapper package. Any added
NuGet/CMake/GUI/media binding later must enter this inventory before vendoring.

## Inspected ADB package: evidence, not blanket clearance

Official URL: <https://dl.google.com/android/repository/platform-tools_r37.0.1-win.zip>.
Read `platform-tools/source.properties`: `Pkg.Revision=37.0.1`.
Downloaded archive size: 8,044,989 bytes. This archive was used only for inspection,
outside the MobileMapper repository; binaries were not executed or committed.

| Artifact | Observed SHA-256 |
| --- | --- |
| platform-tools_r37.0.1-win.zip | `45f4d63113e895ebde0c90f194099a4676b6ac653bd28d54314a9e022bbc1a99` |
| adb.exe | `b4a6b455702684652cccf7b46258b29e653538904359a58fd4931cf3ef286b3f` |
| AdbWinApi.dll | `c1d653030b4bde65d3e07e4d0b0979e17be56df1436cdd15528630f27808050d` |
| AdbWinUsbApi.dll | `0710e894d9b40f71a670c13c694079d564c92c1279da382cfe4850983aaebe1b` |
| NOTICE.txt | `38ec8c6f5b7799c223ffeab1f9e81c2d5fc67b5e56d6424f649630ca1ee1a811` |

The archive also contains fastboot, filesystem/other SDK utilities and an aggregated
21,893-line NOTICE with Apache, BSD/MIT-style, GPL/LGPL and other texts. Their presence
does not prove every license applies to adb.exe; it **does** disprove treating the
whole ZIP as one Apache-only component. `objdump -p adb.exe` shows AdbWinApi.dll
and Windows/system runtime imports, but PE imports cannot identify static libraries.
No conclusion about static dependency licensing follows from that import list alone.

Preferred route: bundle only a verified ADB component set with its complete notice
and applicable source/relink material. Before release, establish an authoritative
per-component build/source mapping for this artifact, or build ADB from pinned
AOSP sources with an auditable dependency manifest. Include libusb/crypto/mDNS/
compression/protobuf and USB helper licenses **if actually present**; do not infer
their versions from a mismatching AOSP branch. No need to ship fastboot/filesystem
tools. In Phase 1 developer testing, an explicitly selected installed ADB is acceptable.

## FFmpeg release policy

Produce a reproducible shared-library build for the selected Windows architecture.
Record full `configure` output, source revision, compiler/patches and SHA-256s.
Explicitly disable GPL, nonfree and version3 components; start from a minimal
allowlist for H.264 decoding and D3D11VA, without x264/x265 encoders. H.264 decoding
does not require libx264. Audit automatic/external dependencies as well as flags.
Keep the DLL interface replaceable and do not make signature checks a prohibition
on users exercising LGPL replacement rights. An EULA must preserve required
reverse-engineering rights for debugging library modifications.

Host the exact corresponding FFmpeg source and build materials alongside the
binary distribution; a link to an unrelated current FFmpeg homepage is insufficient.
Ship relevant license texts and attribution; apply the same check to enabled
LGPL dependencies. If the selected build unexpectedly enables GPL or nonfree code,
stop packaging and re-evaluate—renaming a DLL or calling it a subprocess is not
a license workaround. Codec patent questions are separate from copyright licenses
and remain a distribution-market review item [L6].

## Release checklist and project license

- Create a machine-readable SBOM and exact artifact manifest when actual dependencies
  are added. Preserve upstream notices and modifications; match binary hashes to
  source archives and reproducible build recipes where required.
- Include all required runtime/native/installer notices in both installed and
  portable distributions. Validate license and source links from a clean machine.
- Sign the application's executable/DLL/installer as appropriate; do not treat
  signing as satisfying third-party licensing or guaranteeing SmartScreen reputation.
- Repository baseline has **no root LICENSE**. Phase 0 has not silently chosen a
  project license. The owner must decide the MobileMapper source distribution
  license before a public source/binary release policy is finalized.

The current commit distributes only original documentation and experimental Python.
Release readiness remains pending artifact-specific checks; this distinction is
part of the architecture decision, not a claim that the selected technologies
cannot be distributed.

## Phase 1 actual dependencies (2026-10-09)

The Phase 0 statements above describe that historical documentation-only commit.
Phase 1 introduces NuGet/build dependencies and a developer artifact. No third-party
binary is committed to Git. `dependencies.lock.json` pins the selected components;
`artifact-manifest.json` generated inside each successful artifact hashes every
shipped payload. `third-party/nuget-manifest.json` records exact resolved package
versions/content hashes; component license/NOTICE/nuspec files are collected from
that actual dependency closure. ADB is **not** redistributed; its earlier gate remains.

| Component | Actual selection and artifact provenance | Developer packaging |
| --- | --- | --- |
| scrcpy server | 5.0.1, commit `a60891aea193d92e7e5c3942700eca63f9d19a5f`; upstream GitHub release server | Apache-2.0 LICENSE; SHA-256 `764eb6f79811d5211fe9df341120882ba9994c7a61b897d7bf3fb662e53bc536` checked at build and each launch |
| FFmpeg | 9.0.2, vcpkg port 1, `avcodec` + `swscale`, default features disabled, shared x64-windows | LGPL-2.1-or-later; no GPL/nonfree/version3 features or external x264/x265. CPU decoder selects H.264. Upstream port enables other internal LGPL codecs/platform accelerators; this is not a hand-minimized H.264-only build. |
| vcpkg | `0699a19d0c6386247ce50d4dedbb8217d484d536` | MIT build tool; exact full vcpkg source archive includes applied FFmpeg patches and build recipes. Build tools are not installed application dependencies. |
| Windows App SDK | Microsoft.WindowsAppSDK NuGet 2.5.1 and its exact restored dependencies | **Binary NuGet terms are Microsoft Software License Terms**, not simply the repository's MIT. Inspected package `license.txt` §3(a)(i) permits files binplaced by the package in self-contained/framework-dependent apps; distribution requirements/restrictions still apply. Package licenses and NOTICEs accompany output. |
| .NET runtime | .NET 10 self-contained win-x64 runtime selected by the installed supported SDK | MIT plus actual runtime third-party notices; preserve pack LICENSE and THIRD-PARTY-NOTICES, record exact resolved runtime/output hash. |
| Microsoft.NET.Test.Sdk / xunit / VS adapter | 17.14.1 / 2.9.3 / 3.1.1 | Test/build only; not copied to developer app output. MIT upstream/package licenses. |
| MSVC runtime | Current supported official x64 VC++ Redistributable, installed by user | Not bundled. Its separate Microsoft license/install flow applies. Windows D3D/DXGI DLLs come from the OS. |

FFmpeg exact upstream archive:
<https://github.com/ffmpeg/ffmpeg/archive/n9.0.2.tar.gz>.
Expected SHA-512 from the pinned port:
`21bf3fbcdfd2f41ea6edeab40433fecfe362b09ecaa177a652471b54f9357011b4f74dab18245ab1c26f1a473b94205490baecb42289afd2effd603cd0b22b59`.
The archive plus exact vcpkg source (ports/ffmpeg contains the patch list/files),
manifest and build scripts are included **inside the same developer ZIP**. This
makes corresponding source available wherever the binary artifact is available,
including cache-hit builds. Native tests inspect `avcodec_license/configuration`
and reject GPL/nonfree/version3-enabled builds before packaging. DLLs remain
replaceable, and no EULA restriction on debugging modifications to LGPL libraries
is imposed. Release-market patent review and the owner's project license decision
remain Phase 3 gates.

Official license evidence: [FFmpeg legal](https://ffmpeg.org/legal.html),
[pinned vcpkg port](https://github.com/microsoft/vcpkg/blob/0699a19d0c6386247ce50d4dedbb8217d484d536/ports/ffmpeg/portfile.cmake),
[scrcpy LICENSE](https://github.com/Genymobile/scrcpy/blob/a60891aea193d92e7e5c3942700eca63f9d19a5f/LICENSE),
[WindowsAppSDK 2.5.1 NuGet](https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.5.1).
The actual WindowsAppSDK nupkg license and transitive notices take precedence over
an inference from a repository badge. Developer artifacts are not a declaration
that all public product release/signing/license-policy gates have been closed.
