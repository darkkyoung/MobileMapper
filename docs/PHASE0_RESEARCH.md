# Phase 0 research and validation record

Date: 2026-10-08. Canonical baseline: `77cf44a675368137d2c84852129da42b664218f5`.
Initial tracked files were README.md, AGENTS.md and .gitignore; worktree was clean.
All three were read before changes. No earlier conversation was used as evidence
of implemented code. [ARCHITECTURE.md](ARCHITECTURE.md) is the selected design;
[SOURCES.md](SOURCES.md) identifies the official evidence behind this report.

## Decision and completion boundary

Select C#/.NET 10 + WinUI 3, a C++ media DLL using shared FFmpeg and D3D11,
managed ADB wireless lifecycle, and a custom client for pinned scrcpy server 5.0.1.
The architecture and offline validation deliverables are complete. **The full
physical critical-path acceptance gate is not passed.** This repository is ready
for the first Phase 1 risk slice, not a claim of a working Windows game controller.

This environment is Linux with Python 3.12.14 and system FFmpeg 6.1.1. It has no
.NET SDK, Windows graphics runtime or connected Android/ADB test device. No
Windows build, wireless pairing, hardware decoding or Android event injection
was run. Those absences constrain the conclusion, not the reported test results.

## A. Windows desktop alternatives

The following is an engineering comparison, not performance benchmark results.
Every candidate can call native code; GC or language alone does not determine
latency. The video queue/texture path and input ownership are more consequential.

| Candidate | Video + codec integration | Input and window behavior | Editor / DPI | Tooling, upkeep and distribution | Outcome |
| --- | --- | --- | --- | --- | --- |
| C#/.NET + **WinUI 3** | SwapChainPanel is a DirectX composition surface; narrow C++ FFmpeg/D3D bridge avoids per-frame managed copies | HWND interop for Raw Input, focus/global hotkey policy, AppWindow borderless/fullscreen | XAML controls coexist with viewport; handle composition scale, overlay hit tests and focus explicitly | Managed state/profile debugging plus native debugger; Windows App SDK lifecycle and two self-contained runtimes add deployment work | **Selected:** best fit for Windows-only video + future visual editor [W1,W4,W5] |
| C#/.NET + WPF | HwndHost+D3D11 can be efficient; D3DImage takes D3D9 interop, not direct modern D3D11 composition | Mature Win32 hooks, borderless/focus APIs; Raw Input is possible | Child-HWND airspace prevents normal WPF overlay composition. Native overlay or interop/copy strategy needed | Mature XAML/MVVM and excellent tooling; simpler runtime packaging, but viewport/editor workaround becomes permanent | Strong fallback only if WinUI risk slice fails; not rejected for slowness [W6] |
| C#/.NET + Avalonia | Custom GPU/native host integration possible; FFmpeg still needs explicit native lifetimes | Platform abstraction plus Windows-specific raw-input hook | Cross-platform UI useful only if requirements change; validate renderer texture interop and scaling | MIT core, extra Skia/backend/native dependency review; good managed maintainability | Cross-platform value does not currently offset another graphics abstraction [L4] |
| C++ + Qt Widgets / Qt Quick | Direct FFmpeg APIs; Qt Quick native texture/render-thread integration is viable | Native event filter supports Raw Input; fullscreen/window controls available | Qt Quick scene graph can compose controls and video; Widgets/native-window mixing needs care | One application language, good native debugger; larger C++ ownership surface, QML scene-graph constraints, Qt DLL/module licensing/deployment | Viable runner-up if team wants all-C++; no technical inability asserted [W7,L3] |
| Rust + native UI (e.g. winit/wgpu family) | FFmpeg/D3D/WinRT FFI required; choose one graphics ownership model | Strong control over event/state ownership, raw Windows APIs still needed | Editor controls/accessibility/layout require assembling and validating a GUI stack | Memory-safety benefit in owned code; unsafe media/COM boundary remains, Windows GUI integration/debugging effort is higher | Not selected; Rust does not remove the hard interop path |
| Rust + Tauri / Electron-style hybrid | WebView video/canvas or native surface bridge; packet/native/GPU transfers must be bounded | Relative mouse and global capture need native integration; browser events alone insufficient | Web UI editor convenient, but native overlay composition is another integration problem | WebView/JS IPC, multiple runtimes and security/update surface; FFmpeg still native | No justification for web runtime here; do not say browsers inherently cannot be low-latency |
| Pure C++ Win32 + DirectX | Most direct resource control | Full Win32 access | Entire editor, accessibility and settings UI must be built/integrated | Lowest framework dependence, highest product-UI effort | More reinvention than needed |

High-frequency input is Win32 Raw Input in all serious candidates; global capture
does not depend on the UI toolkit. Focused capture is the product default. Windows
packaging, FFmpeg native DLLs and upstream scrcpy version management remain work
in every option. WinUI selection trades a more involved runtime package for avoiding
a known WPF video/editor composition obstacle. Native ABI and small ownership scope
keep future framework changes possible without replacing mapping/profile code.

## B. Wireless ADB findings and choices

Official Android docs establish Android 11+ wireless pairing without prior USB [A1].
The AOSP architecture distinguishes TLS pairing and connect service types [A2].
The program can automate discovery and reconnect behind UI; it cannot enable
Developer Options, restore revoked trust, or bypass network isolation for users.

| Approach | Assessment |
| --- | --- |
| Bundled verified ADB component set | Selected distribution direction: consistent command/output version and no developer installation UX. Binary/source/notice mapping must be cleared first. |
| User-installed Platform-Tools selected in settings | Phase 1 developer fallback; verify capabilities/version. No terminal commands required, but installation friction prevents making it the final default. |
| Download official tools at first launch | Adds network/EULA/provenance/update handling; not automatically a solution to redistribution obligations. Not selected as normal UX. |
| Reimplement ADB/TLS/mDNS in a library | Adds authentication, discovery and protocol maintenance. No present benefit over subprocess control. |
| Legacy `adb tcpip 5555` | Often requires USB bootstrap and is not the Android 11 TLS flow. Rejected for normal use. |

Pairing and connect ports must be separate UI concepts. Discovery is refreshed
after reboot, Wi-Fi changes or Wireless Debugging toggles. Saved records are hints;
authentication is ADB's responsibility. Multiple devices require explicit selection
and serial-scoped commands. Errors distinguish code expired, trust revoked, device
offline, blocked mDNS, wrong endpoint and shell/input permission failure.

Current-source wrinkle: Platform-Tools 37.0.1 notes say `libadbmdns` replaces
Openscreen and `ADB_MDNS_OPENSCREEN` no longer has an effect [A3]. The older AOSP
Wi-Fi design/source pages still mention Openscreen. Use the design for TLS/service
semantics, not as an exact dependency manifest for this Windows binary. Do not
copy old advice to force Openscreen/Bonjour. Test actual CLI output, including
extra hostname columns, before freezing its parser.

Android's 2026 Wi-Fi 2.0 announcement describes newer Android 17/tooling behavior
[A4]. That is an additional compatibility row, not permission to promise its
reconnect behavior on every Android 11 device. Existing Android 11+ remains the
baseline. QR pairing is a later UX enhancement; code pairing is sufficient first.

## C. scrcpy integration alternatives

| Method | Complexity / latency | GUI and input control | Upstream / distribution | Decision |
| --- | --- | --- | --- | --- |
| Launch stock scrcpy externally | Lowest bootstrap cost; proven pipeline but measurements still device-dependent | Separate window, separate focus/control ownership | Whole client SDL/FFmpeg/ADB bundle and licenses | Diagnostic baseline only |
| Reparent/hide external scrcpy HWND | Initially small wrapper, later fragile lifetime/DPI/focus handling | Cross-process editor overlay and mapping ownership awkward | Window heuristics depend on upstream behavior | Rejected as product architecture |
| Pinned server + own small client | Moderate/high startup cost; direct packet/decode/render pipeline | Fully owned surface and multi-touch writer | Explicit protocol-upgrade work; no stable public ABI | **Selected**, reuse Android capture/injection and codecs, isolate protocol |
| Incorporate/fork scrcpy C client | Reuses demux/control code | Refactor SDL main-loop/screen/input globals into library-facing API | No official embeddable library ABI; maintain fork, notices and modified-source markers | Reserve selective adaptation only when it saves demonstrated work |
| New Android companion capture/control app | Largest engineering and user setup cost | Ordinary app permissions cannot freely inject arbitrary multi-touch | MediaProjection/permissions/service lifecycle still unsolved | Rejected absent a concrete need; do not rebuild scrcpy's privileged path |

The chosen method and “scrcpy-compatible client” are the same practical family,
not two unrelated proposals. The server runs temporarily under the authorized ADB
shell; it is not installed as a normal app. Upstream uses hidden Android APIs, so
OEM/version incompatibility remains possible [S1]. APK/process/memory modification
and anti-cheat bypass remain out of scope.

Important source finding: release **5.0.1** is commit
`a60891aea193d92e7e5c3942700eca63f9d19a5f`. Its developer document still labels parts
of its examples “4.0”; the actual source/version wins. We inspected Options,
DesktopConnection, Streamer, demuxer, packet merger, control serializer/reader,
Controller, PointersState and PositionMapper at the pinned commit. No upstream
production source was copied into this repository.

## D. Video alternatives and bottlenecks

| Option | Reasoned assessment | Selection |
| --- | --- | --- |
| H.264 | Broad Android/Windows codec path, adequate local-network quality | Baseline |
| HEVC | Potential bandwidth savings; support/encode/decode latency must be measured | Later opt-in |
| AV1 | Device encoder availability and real-time cost vary; no bandwidth need proven | Deferred |
| FFmpeg software decode | Portable correctness baseline, CPU cost and uploads | Required fallback and initial native bring-up |
| FFmpeg D3D11VA | Windows hardware path available through FFmpeg API; texture ownership/sampling varies [V1] | Preferred after benchmark |
| Media Foundation H.264 MFT | Windows platform decoder/D3D integration avoids FFmpeg redistribution; MFT configuration, output changes and separate fallback/capability work [V2] | Rejected as first backend; viable fallback if FFmpeg distribution becomes impractical |
| OpenGL / Vulkan renderer | Technically possible but introduces cross-API GPU interop on Windows | Rejected without a cross-platform requirement |

The pipeline spends time in phone capture/encode, network/ADB, host decode, GPU
presentation and phone game response. Hardware decode and high bitrate alone do
not solve it. Preserve compressed reference chains; only drop stale **decoded**
frames freely. TCP can stall control behind network congestion even with separate
sockets. Bounded queues, latest-frame presentation and reconnect/backlog policy
are architecture requirements. Audio is explicitly deferred, avoiding unnecessary
audio/video synchronization latency in the first proof.

Targets and measurement method are in ARCHITECTURE section 5. They are acceptance
goals for a good local network, not an advertised 30 ms promise or achieved FPS.

## E–H. Input, coordinates and mapping conclusions

Source inspection confirms persistent arbitrary pointer IDs and a ten-pointer
table in the selected server [S5,S6]. The Android controller composes simultaneous
MotionEvents from separate per-pointer messages. This supports the proposed
movement+aim+tap architecture **at the protocol/code level**. It does not prove
that a specific OEM/game accepts it. Some OEMs require additional debugging input
permissions; permission failure is a supported error, never a bypass opportunity.

Launching `adb shell input tap`/`swipe` per event adds process/shell round trips
and cannot provide our coordinated long-lived pointer ownership. Even a persistent
shell does not provide the required general multi-touch semantics through those
commands. It is rejected for the real-time mapping transport.

Raw Input supplies relative movement; cursor lock alone does not create FPS-like
input [W2]. Background/global capture is technically possible but not the default.
The control engine has a shared allocator and ordered writer with release barriers.
There is no guarantee of releasing touches across a physically broken link.
Upstream Controller only removes finger state on UP; treating CANCEL as a complete
reset without proof would be unsafe. Rotation can invalidate an UP's dimensions.
Those are first-slice acceptance risks, not silently solved by local unit tests.

Coordinates are normalized within displayed Android content, including reference
aspect/orientation; radii use the short edge. Input dimensions are encoded video
dimensions, not Windows screen pixels and not an extra scaling to native Android
resolution [S7]. Letterboxing and DIP scale belong in the viewport transform.
UI reflow at a changed aspect ratio requires calibration. Primitives share a pure
event/state framework; trigger and fire lifecycle are profile data. A profile
name/package identifier must never trigger game-specific engine behavior.

## I–J. Distribution and license conclusions

Choose a self-contained app directory and Inno Setup. App SDK and .NET are separate
runtime payloads; native DLLs remain side-by-side. Code signing is planned for
release; there is no “unsigned executable always blocked” or “signed means Defender
never warns” claim. Full license decisions and unresolved artifact-specific gates
are in [THIRD_PARTY.md](THIRD_PARTY.md).

We actually downloaded official Windows Platform-Tools **37.0.1**, verified its
source.properties, inspected the ZIP, NOTICE and adb.exe import table, and recorded
SHA-256 hashes. The package NOTICE aggregates many licenses without a sufficient
per-binary mapping. Thus **binary redistribution is not cleared by this research**.
Preferred final UX remains a bundled, reviewed subset; a source-built ADB with a
complete dependency/SBOM record is the fallback if provenance cannot be established.
That is a release/dependency gate, not a request for users to operate terminals.

## Executed PoC and results

Source: [experiments/phase0](../experiments/phase0/README.md). Python is a convenient
executable protocol specification here, **not** a change to the C# product stack.

| Experiment | Observed result | Proves / does not prove |
| --- | --- | --- |
| `python3 -m unittest discover -s experiments/phase0 -v` | **20 tests passed** on Python 3.12.14 | Original code matches inspected wire layouts and local invariants; not execution of the upstream Android server |
| Protocol vectors | 32-byte touch golden vector, pressure, fragmented records, all truncation boundaries, malformed size/session rejection, socketpair read passed | Wire/data handling; not ADB stream capture |
| Contact/geometry model | Three independent IDs, ten-contact cap, UP emission, generation rejection, normalized endpoints, DPI/letterbox, rotation roundtrip, diagonal normalization passed | Local intent coordination; not Android release acknowledgment |
| Discovery fixtures | Different pair/connect ports, refreshed endpoints, extra columns, multiple devices, IPv6 and invalid port handling passed | Parsing example data only; no actual discovery or reboot test |
| `python3 experiments/phase0/decode_probe.py` | **24 H.264 frames decoded**, two synthetic sessions (160×90, 90×160), identical frame CRCs before/after framing + socket + demux | Real Linux software decoder accepted reconstructed data; not MediaCodec output, live rotation, D3D11, C ABI or performance |

The decode fixture uses local FFmpeg 6.1.1's libx264 **only to generate test data**.
It contains in-band codec configuration and AUD-delimited access units. Separate
config framing is covered by parser tests but not an actual native AVCodec packet
adapter. No generated media, third-party source, dependency binary or captured
device information is committed. A CPU subprocess decode is an experiment, not
the selected product rendering mechanism.

No .NET/C++ project skeleton was generated: it could not have validated the chosen
Windows path here and would create false implementation progress. Offline tests
are rerunnable without a phone; codec probe requires a developer FFmpeg with libx264.

## Windows / Android acceptance checklist

Owner: the next Phase 1 implementer with Windows and physical Android access.
Store a redacted test report with exact versions, settings, steps and observed
results. Initial matrix: Windows 11 x64, integrated GPU plus one discrete GPU when
available; Android 11 baseline plus a recent Android/OEM device. A newer Wi-Fi 2.0
device is a separate row. One phone validates only that phone, not all Android.

| Priority / risk | Procedure | Required evidence / failure response |
| --- | --- | --- |
| P0 — WinUI/native media | Build minimal app, attach swap chain, feed test stream, overlay a marker, resize, move between 100/150/200% monitors, test CPU then hardware | Build log, correct marker/hit position, decoder/backend metrics, no leaks/device-loss crash. If interop fails, repair boundary before UI expansion; WPF/native overlay is documented fallback. |
| P0 — Wireless first use | Forget only the test app's intended phone pairing; pair using UI and code with USB unplugged; stream | Pair/connect endpoints handled distinctly; authenticated transport and video. Never delete unrelated PC keys to run this test. |
| P0 — Multi-touch | On a benign multitouch test surface: hold pointer A, move B, tap/release C, release B, then A; repeat 100 times | Android visibly shows independent lifetimes/coordinates, correct three-pointer state, no residual pointers; distinguish server parse from injection permission errors. |
| P0 — Focus/emergency | Hold movement+aim, Alt+Tab, minimize, Escape, switch profile/edit mode, lock/suspend | Local cursor restored immediately; phone contact indicators clear on intact link. Old queued input never reappears. |
| P0 — Hard disconnect | Hold contacts, turn Wi-Fi off, terminate desktop, stop server, then reconnect | Record actual phone state, not just empty local ledger. Failure blocks release-quality input; implement/test a narrowly scoped server cleanup/watchdog if stock cannot meet policy. |
| P0 — Geometry | Resize PC, rotate/fold phone while idle and while contacts held, including 180° where supported | No stale-coordinate taps or double rotation; input pauses/calibration policy visible; new session releases verified. |
| P1 — Rediscovery | Reboot phone, toggle Wireless Debugging, change IP/network, revoke trust | Fresh endpoint used, bounded retries, correct NeedsPairing versus NeedsUserAction; no repeated manual IP entry on a working discovery network. |
| P1 — Multi-device/coexistence | Two paired phones and Android Studio's ADB running | Explicit selection; no wrong-device touch, no termination of unrelated daemon, owned forwards removed. |
| P1 — Bad network / decoder | Block multicast, introduce congestion, unsupported hardware decode, graphics reset | Friendly manual fallback, bounded memory/backlog, CPU fallback/lower preset, controlled recovery. |
| P1 — Latency | Camera-based tests described in architecture, >=100 input samples; 10 min motion + 30 min stability | Report medians/p95/drops with environmental data; miss -> tune media path before claiming game-ready. |
| Release — Dependencies | Match each shipped artifact to license/source/notices; clean VM install/uninstall and portable launch; inspect signatures | Source bundles/relink material where required, no missing runtimes, no keys/logs bundled. Gate remains open until actual artifacts exist. |

## Review outcome and remaining blockers

Source-backed architecture is sufficient to begin a **narrow risk slice**. Full
Phase 0 physical-path certification remains blocked on Windows/Android access.
The most important unproven claim is not JSON/profile feasibility: it is the
combined WinUI GPU path + real wireless input behavior, especially hard-disconnect
release. Do not expand to a full mapping editor until those tests inform the design.

Exact redistributable ADB provenance and the eventual FFmpeg build configuration
remain packaging gates. The project itself also has no root LICENSE yet; no
project license was chosen on the owner's behalf. These findings do not stop local
developer testing with legitimate dependencies, but do prevent claiming a ready
public installer. See architecture section 10 for Phase 1 order; no Phase 1 product
implementation has been started in this change.
