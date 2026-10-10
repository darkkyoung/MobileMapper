# Phase 1 implementation — Wireless Mirroring MVP

Status: implementation and Windows x64 build/test/publish verified; **Windows interactive and Android physical acceptance NOT TESTED**.
No latency/FPS measurement or claim that pairing/touch works on Galaxy S26 follows
from compilation. Phase 2 must wait for the device checklist.

## Actual projects

| Project | Responsibility |
| --- | --- |
| MobileMapper.App | WinUI 3 unpackaged x64 shell, settings, pointer capture, SwapChainPanel COM attachment, diagnostics controls |
| MobileMapper.Device | Explicit official adb.exe path, private foreground daemon, process adapter, discovery/pairing/connect, selected-device commands |
| MobileMapper.Scrcpy | Pinned artifact verification/bootstrap, framing, pooled packets, bounded packet queue, control serializer/writer |
| MobileMapper.Core | Coordinate math, touch ownership/generations, session states, bounded session log |
| MobileMapper.Session | Selected-device lifecycle, capped recovery, network/decoder workers and input safety boundary |
| MobileMapper.Media | Narrow C ABI P/Invoke ownership wrapper; no FFmpeg structs or managed pixel buffers |
| MobileMapper.Media.Native | C++20 FFmpeg CPU decoder, latest AVFrame slot, render worker, color conversion/upload and D3D11 composition swapchain |
| MobileMapper.Tests | Device/protocol/input/geometry/lifecycle pure-logic tests |

No empty Mapping/Profile projects, game profiles, editor, relative mouse aim or
WASD mapping were added. Phase 1 uses XAML pointer events for direct touch;
Raw Input/high-frequency relative mouse capture remains the Phase 2 design.

## Runtime flow

1. User selects official Windows Platform-Tools `adb.exe`. MobileMapper starts its
   **own foreground daemon** on an ephemeral loopback port and directs every ADB
   client to that port. It never issues global `kill-server`; Android Studio's
   daemon is not intentionally stopped. A bind/start failure is reported.
2. Refresh queries `mdns services`, distinguishes pairing/connect services, attempts
   authorized connection endpoints and lists `devices -l`. Manual pairing and
   connection endpoints remain separate. The password box is cleared immediately;
   the code is sent through stdin, never argv, settings, logs or exports.
3. The user explicitly selects an online device. `ro.serialno` is the saved identity
   hint (not IP:port). If unavailable, mirroring requires investigation rather than
   reconnecting to an arbitrary phone. Settings store only ADB path/identity in
   LocalAppData/MobileMapper/settings.json. ADB manages host authentication keys in
   its normal user location; MobileMapper never reads or exports private key data.
4. SHA-256 check precedes every server push. A random 31-bit scid identifies a unique
   temporary JAR and `localabstract:scrcpy_<scid>` endpoint. `forward tcp:0` is owned
   by that session. The server runs with H.264, max_size=1280, max_fps=60, 8 Mbps,
   audio=false, control=true, clipboard_autosync=false and cleanup=true.
5. Connect video, read the zero dummy byte, connect control, **then** read name/codec
   metadata. This order avoids the server's accept-before-metadata deadlock.
6. The managed parser uses exact reads, accepts arbitrary fragmentation, validates
   H.264/session flags/dimensions, bounds every packet to 16 MiB and returns pooled
   compressed bytes. Config and session records retain ordering. Screen names and
   clipboard content are discarded, not logged.
7. A channel holds at most 8 records and 16 MiB of queued compressed payload. There
   can additionally be one receiving and one decoding packet, each <=16 MiB.
   A full queue or packet age >250 ms restarts the stream; arbitrary H.264 packets
   are never silently dropped. Channel contents are disposed after workers stop.
8. A decode worker passes the compressed span into the native DLL. Native FFmpeg
   owns packets/decoder; config bytes precede the next access unit. CPU H.264 decode
   uses slice threading (two threads). A separate render worker retains only the
   latest decoded AVFrame plus its currently presented frame. libswscale converts
   to BGRA, D3D11 uploads a texture and draws an aspect-fit triangle into the
   SwapChainPanel swapchain. Black bars, resize and composition scaling are explicit.
   Present uses vsync, maximum frame latency 1 and two buffers. No UI-thread decode
   or managed decoded-frame copy. D3D11VA is **not implemented** in this milestone.
9. Input is paused until an explicit **Enable touch** action and a displayed frame
   from the current geometry generation. DIP pointer coordinates remove letterbox
   offsets, normalize, and map to current video dimensions; scrcpy maps to Android.
   Down/Move/Up use unique IDs and zero mouse-button fields. Diagnostic A/B/C use
   the same allocator and writer as direct touch.

## Failure handling and limits

The session state machine covers Idle, Discovering, Pairing, Connecting,
StartingServer, Streaming, Recovering, NeedsPairing, NeedsUserAction and Stopping.
Pair/discovery UI operations show their states directly; mirroring uses the tested
transition machine. These are not independent booleans controlling transport.

Escape, app deactivation/Alt+Tab, minimize, direct-viewport focus/capture loss,
Disconnect, teardown and shutdown clear local contacts and increment input generation.
A priority writer barrier drops unsent intent, then sends UP for contacts actually
written to the socket. MOVE coalescing is limited to adjacent moves for the same
pointer. The control queue is capped at 128 commands; writes time out at 500 ms,
release wait is bounded at 650 ms. A dead link cannot guarantee remote UP delivery.
Recovery always starts with fresh sockets, server, geometry and paused input; stale
commands never replay. Rotation while contacts are held releases and restarts.

Automatic recovery performs fresh discovery and identity comparison, with at most
five retries after the initial attempt and delays 1/2/4/8/15 seconds. A user
Disconnect cancels the loop. After exhaustion, refresh/manual connection/pairing is
required. OEM restrictions, secure content, Wi-Fi client isolation, blocked mDNS,
private daemon coexistence and scrcpy hidden-API compatibility require device tests.
A selected-device no-op shell heartbeat every two seconds has a three-second timeout
to detect broken wireless transport even when the screen is static. A stopped private daemon requires app restart. A D3D device/presenter failure also
requires app restart; device-lost recreation is not yet implemented.

Diagnostics are session-bounded (200 log entries) and in-memory; no disk debug log,
recording, clipboard sync or screenshots. Resolution/FPS counters are measured
inside the pipeline, not glass-to-glass latency. Presented FPS counts successful
Present calls, not proof of compositor display. Device identifier is shown in the
UI for selection, not exported in logs. Error text never includes raw ADB output.

## Build and CI

Requirements: Windows 11 x64; .NET 10 SDK; Git; CMake >=3.24; Visual Studio 2022 or
newer Build Tools with **Desktop development with C++**, MSVC x64 and Windows SDK
10.0.26100 or newer; PowerShell 7. Internet access is needed for NuGet/vcpkg/source.
Use a checkout directory **without spaces** (upstream FFmpeg port restriction).
From a Developer PowerShell with those tools available:

```powershell
git clone https://github.com/darkkyoung/MobileMapper.git
cd MobileMapper
pwsh -File scripts/build.ps1 -Configuration Release
.\artifacts\MobileMapper\MobileMapper.App.exe
```

The script pins vcpkg, builds shared FFmpeg and C++ media, runs CTest and managed
tests, publishes the self-contained x64 WinUI app, verifies/downloads the server,
and packages third-party notices, source/build recipes and an artifact hash manifest.
The first FFmpeg build can take tens of minutes. Never copy a GPL FFmpeg download
into the output as a shortcut. MSVC's current x64 Redistributable is a user-installed
prerequisite; no arbitrary Windows system DLLs are copied.

`.github/workflows/windows-build.yml` uses windows-latest, .NET 10, x64 Release,
CTest, managed tests and developer artifact upload. Device-free CI needs no secrets.
The full solution is `MobileMapper.slnx`; the app project reference graph compiles
all production managed modules, and the test project compiles/tests pure logic.

Verified full implementation run: https://github.com/darkkyoung/MobileMapper/actions/runs/38032973345
at commit `056d5c02281c2906f17a76bf2d996b6eca6a84a3`: Windows x64 native build,
CTest 1/1 (14 native assertions), managed tests 65/65, WinUI Release publish and
`MobileMapper-win-x64-developer` artifact all succeeded. Subsequent safety/documentation
commits must also pass the workflow; use the successful run for the current main SHA.
Initial skeleton run 37882378470 had no managed test cases and is not MVP evidence.
A green compile does not mean a WinUI window was interactively launched in CI.

## Test evidence

- Linux .NET 10: 65/65 managed tests passed; no skipped tests (initial implementation).
- Windows C++: CTest 1/1 passed (14 assertions). The native test program checks ABI/errors, runtime LGPL/config flags,
  WARP D3D11 composition swapchain creation, synthetic H.264 decode, ownership/reset.
  This is headless validation, not SwapChainPanel/device acceptance.
- Phase 0 Python experiments remain executable specifications, not production deps.
- `test_frame.h` is an original synthetic red 32×32 H.264 access unit, generated by:

```sh
ffmpeg -f lavfi -i color=c=red:s=32x32:r=1 -frames:v 1 -c:v libx264 -preset ultrafast -tune zerolatency -f h264 red.h264
```

Only the generated bytes as a text fixture are checked in. The generator's GPL
FFmpeg/libx264 binary and source are not product dependencies or redistributed.

## Phase exit

Run [PHASE1_DEVICE_TEST.md](PHASE1_DEVICE_TEST.md) on Galaxy S26/Windows 11. The
high-risk gates are real WinUI composition, wireless authorization/reconnection,
coordinate correctness under rotation/DPI, simultaneous contacts and abrupt-link
remote input cleanup. Do not start Phase 2 product features until those gates pass.
