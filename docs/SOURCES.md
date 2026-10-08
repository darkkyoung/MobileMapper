# Phase 0 primary-source register

Accessed 2026-10-08. IDs are referenced from architecture/research/license tables.
Repository implementation facts come from the actual checked-out source where
specified. A linked feature is not a claim that MobileMapper has implemented it.
Moving documentation/license URLs must be rechecked when choosing release artifacts.

## scrcpy: pinned 5.0.1 source

All S references below use commit `a60891aea193d92e7e5c3942700eca63f9d19a5f`,
verified locally as tag `v5.0.1`. [Release page](https://github.com/Genymobile/scrcpy/releases/tag/v5.0.1).

- **S1:** [Developer architecture / internal protocol](https://github.com/Genymobile/scrcpy/blob/a60891aea193d92e7e5c3942700eca63f9d19a5f/doc/develop.md).
  Describes shell server, separate sockets and exact version match. Some prose/examples
  still say 4.0; source definitions below determine the 5.0.1 adapter.
- **S2:** [DesktopConnection.java](https://github.com/Genymobile/scrcpy/blob/a60891aea193d92e7e5c3942700eca63f9d19a5f/server/src/main/java/com/genymobile/scrcpy/device/DesktopConnection.java)
  and [Options.java](https://github.com/Genymobile/scrcpy/blob/a60891aea193d92e7e5c3942700eca63f9d19a5f/server/src/main/java/com/genymobile/scrcpy/Options.java).
  Socket order, dummy byte, device name, server flags and version enforcement.
- **S3:** [Streamer.java](https://github.com/Genymobile/scrcpy/blob/a60891aea193d92e7e5c3942700eca63f9d19a5f/server/src/main/java/com/genymobile/scrcpy/device/Streamer.java)
  and [demuxer.c](https://github.com/Genymobile/scrcpy/blob/a60891aea193d92e7e5c3942700eca63f9d19a5f/app/src/demuxer.c).
  Codec ID, session records, media flags, PTS and payload framing.
- **S4:** [packet_merger.c](https://github.com/Genymobile/scrcpy/blob/a60891aea193d92e7e5c3942700eca63f9d19a5f/app/src/packet_merger.c).
  Configuration accumulation and merging before media decode.
- **S5:** [control_msg.c](https://github.com/Genymobile/scrcpy/blob/a60891aea193d92e7e5c3942700eca63f9d19a5f/app/src/control_msg.c),
  [control_msg.h](https://github.com/Genymobile/scrcpy/blob/a60891aea193d92e7e5c3942700eca63f9d19a5f/app/src/control_msg.h),
  [ControlMessageReader.java](https://github.com/Genymobile/scrcpy/blob/a60891aea193d92e7e5c3942700eca63f9d19a5f/server/src/main/java/com/genymobile/scrcpy/control/ControlMessageReader.java),
  [upstream serialization tests](https://github.com/Genymobile/scrcpy/blob/a60891aea193d92e7e5c3942700eca63f9d19a5f/app/tests/test_control_msg_serialize.c).
  32-byte touch wire format and reserved pointer IDs. Local golden vector uses
  ordinary finger button fields zero; it is not the upstream mouse-button test.
- **S6:** [Controller.java](https://github.com/Genymobile/scrcpy/blob/a60891aea193d92e7e5c3942700eca63f9d19a5f/server/src/main/java/com/genymobile/scrcpy/control/Controller.java)
  and [PointersState.java](https://github.com/Genymobile/scrcpy/blob/a60891aea193d92e7e5c3942700eca63f9d19a5f/server/src/main/java/com/genymobile/scrcpy/control/PointersState.java).
  Finger source, pointer-index composition, ten-pointer cap and UP-based table cleanup.
- **S7:** [PositionMapper.java](https://github.com/Genymobile/scrcpy/blob/a60891aea193d92e7e5c3942700eca63f9d19a5f/server/src/main/java/com/genymobile/scrcpy/control/PositionMapper.java)
  and [ScreenCapture.java](https://github.com/Genymobile/scrcpy/blob/a60891aea193d92e7e5c3942700eca63f9d19a5f/server/src/main/java/com/genymobile/scrcpy/video/ScreenCapture.java).
  Video-to-display mapping and stale-size rejection.

## Android / ADB

- **A1:** [Android Debug Bridge documentation](https://developer.android.com/tools/adb).
  Android 11+ wireless pairing, connection commands and troubleshooting.
- **A2:** [AOSP Architecture of ADB Wi-Fi](https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/main/docs/dev/adb_wifi.md).
  TLS, distinct service types, pairing trust and auto-connect; historical backend
  examples must not override newer tool release notes.
- **A3:** [Platform-Tools releases](https://developer.android.com/tools/releases/platform-tools).
  37.0.1 mDNS/backend changes; official download provenance.
- **A4:** [Android Wi-Fi 2.0 announcement](https://developer.android.com/blog/posts/introducing-fast-and-reliable-wireless-debugging-with-android-debug-bridge-adb-wi-fi-2-0).
  Newer Android/tooling behavior is not back-projected onto Android 11.
- **A5:** [ADB commandline source](https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/main/client/commandline.cpp)
  and [auth source](https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/main/client/auth.cpp).
  Pair-code stdin and conventional adbkey handling. This moving source was read,
  but is not asserted to be the exact build source of the inspected 37.0.1 ZIP.

## Windows, rendering and decode

- **W1:** [SwapChainPanel](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.swapchainpanel).
  Native swap-chain attachment, UI scale, focus and composition limitations.
- **W2:** [Raw Input overview](https://learn.microsoft.com/en-us/windows/win32/inputdev/about-raw-input).
  Keyboard/mouse HID registration, foreground/background and batched input reads.
- **W3:** [DXGI flip-model guidance](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/for-best-performance--use-dxgi-flip-model).
  Presentation design; actual WinUI composition timing still requires measurements.
- **W4:** [Windows App SDK self-contained deployment](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps).
  Separate .NET self-contained requirement and native runtime payloads.
- **W5:** [.NET support lifecycle](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
  and [Windows App SDK release channels](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels).
  Research-date stable/LTS selection. SDK package patch pins belong in the first build.
- **W6:** [WPF technology regions](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/technology-regions-overview).
  Airspace constraints of mixed rendering technologies.
- **W7:** [Qt QQuickWindow](https://doc.qt.io/qt-6/qquickwindow.html).
  Scene graph/render-thread and native graphics integration alternative.
- **V1:** [FFmpeg hardware decode example](https://ffmpeg.org/doxygen/trunk/hw_decode_8c-example.html).
  Hardware device selection and hardware frame handling API. Not proof of the planned
  direct GPU rendering path or its performance.
- **V2:** [Windows H.264 decoder](https://learn.microsoft.com/en-us/windows/win32/medfound/h-264-video-decoder).
  Media Foundation alternative.

## License texts and terms actually inspected

- **L1:** [scrcpy Apache-2.0 LICENSE](https://github.com/Genymobile/scrcpy/blob/a60891aea193d92e7e5c3942700eca63f9d19a5f/LICENSE).
- **L2:** [AOSP ADB NOTICE](https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/main/NOTICE)
  and [Android.bp](https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/main/Android.bp).
  Core source license and examples of dependency declarations, not prebuilt attestation.
- **L3:** [Qt LGPL/GPL obligations](https://www.qt.io/development/open-source-lgpl-obligations).
- **L4:** [Avalonia core MIT license](https://github.com/AvaloniaUI/Avalonia/blob/main/licence.md).
- **L5:** [Google Android SDK agreement](https://developer.android.com/studio/terms),
  especially sections 3.4 and 3.5. The exact Windows ZIP and its NOTICE were inspected
  separately; hashes and limits of that inspection are in THIRD_PARTY.md.
- **L6:** [FFmpeg legal page](https://ffmpeg.org/legal.html).
- **L7:** [FFmpeg LICENSE.md](https://github.com/FFmpeg/FFmpeg/blob/master/LICENSE.md).
- **L8:** [.NET runtime MIT license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT).
- **L9:** [Windows App SDK MIT license](https://github.com/microsoft/WindowsAppSDK/blob/main/LICENSE).
- **L10:** [WinUI MIT license](https://github.com/microsoft/microsoft-ui-xaml/blob/main/LICENSE).
- **L11:** [Inno Setup license](https://jrsoftware.org/files/is/license.txt).
- **L12:** [WPF MIT license](https://github.com/dotnet/wpf/blob/main/LICENSE.TXT).

Sources establish technical/license facts. Choices such as module names, WinUI over
WPF, buffer limits, latency targets, field names and phase milestones are
MobileMapper design judgments, not statements endorsed by upstream projects.
