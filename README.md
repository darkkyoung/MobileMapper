# MobileMapper

MobileMapper is a Windows desktop project for using a real Android device from a PC over a local wireless connection.

The goal is to provide:

- low-latency Android screen mirroring on Windows;
- keyboard and mouse input mapped to Android touch input;
- reusable per-game / per-app mapping profiles;
- a visual key-mapping editor similar to the control editors found in Android emulators;
- a normal desktop-app experience without requiring users to operate ADB from a terminal.

MobileMapper is **not an Android emulator**. Games and apps continue to run on the physical Android device.

## Project goals

A typical user flow should eventually be:

1. Install MobileMapper on Windows.
2. Pair an Android device through Wireless Debugging.
3. Launch MobileMapper and reconnect to the saved device.
4. Mirror the Android screen in the MobileMapper window.
5. Select or create a mapping profile.
6. Play or use the Android app with keyboard and mouse input.

The long-term target is a flow close to:

```text
Windows PC
  └─ MobileMapper
       ├─ wireless device discovery / pairing
       ├─ low-latency screen mirroring
       ├─ keyboard + mouse capture
       ├─ touch / multi-touch translation
       └─ mapping profiles
                │
                │ local Wi-Fi / Wireless Debugging
                ▼
           Android device
             └─ game / app
```

## Core requirements

MobileMapper should be:

- **Wireless-first** — normal use must not depend on a USB cable.
- **Game-agnostic** — the mapping engine must not be hard-coded for Brawl Stars or any single game.
- **Profile-based** — mappings can be saved, loaded, duplicated, renamed, and reused.
- **Visual** — users should be able to place controls directly over the mirrored Android screen.
- **Low-latency** — screen and input latency must be suitable for real-time games where the network/device permits it.
- **PC-centric** — avoid requiring a companion Android app unless later investigation demonstrates a clear technical need.
- **Non-invasive** — do not modify APKs, inject into game processes, read game memory, or bypass anti-cheat/security mechanisms.

## Planned mapping primitives

The initial mapping engine is expected to support these general-purpose control types:

- **Tap** — one keyboard or mouse input triggers one touch at a configured position.
- **Hold** — hold an input to keep a touch down.
- **Joystick** — map directional keys such as WASD to a virtual touch joystick.
- **Mouse Aim** — translate mouse direction/movement into a touch joystick or drag region.
- **Swipe / Drag** — map an input to a configured gesture.
- **Multi-touch coordination** — allow compatible mappings to operate concurrently.

Exact behavior and protocol details are architecture decisions and must be validated before implementation.

## Roadmap

The project is intentionally divided into a few large phases rather than many tiny milestones.

### Phase 0 — Foundation & architecture

Establish the engineering foundation before committing to a desktop stack.

Primary work:

- evaluate the Windows UI/runtime stack;
- evaluate how to integrate or interoperate with scrcpy and ADB;
- validate Wireless Debugging pairing, discovery, reconnect behavior, and mDNS options;
- validate the screen transport / decoding / rendering path;
- validate Android input injection, including concurrent touch requirements;
- define the internal coordinate model and profile format;
- review third-party licenses and distribution constraints;
- document the chosen architecture and development workflow;
- create a minimal technical proof of concept where useful to de-risk decisions.

**Exit condition:** the repository has a documented architecture with the critical technical paths validated well enough to begin the product implementation.

### Phase 1 — Wireless mirroring MVP

Build the first end-to-end usable desktop application.

Primary work:

- device discovery and Wireless Debugging pairing;
- saved-device reconnect and connection-state UI;
- mirrored Android screen inside the application;
- low-latency rendering and basic performance controls;
- mouse-to-touch interaction for direct screen control;
- clean disconnect / reconnect behavior;
- diagnostics and actionable error messages.

**Exit condition:** a user can launch MobileMapper, connect to a previously paired Android device without a USB cable, see the device screen, and interact with it from the PC.

### Phase 2 — Universal key-mapping system

Turn the mirroring MVP into the core MobileMapper product.

Primary work:

- visual mapping-edit mode over the mirrored screen;
- Tap, Hold, Joystick, Mouse Aim, Swipe / Drag, and required multi-touch behavior;
- normalized coordinates so mappings remain stable across window sizes;
- keyboard and mouse capture with conflict handling;
- profile create / edit / save / load / duplicate / delete;
- mapping import/export;
- per-profile options such as sensitivity, dead-zone, and joystick radius where relevant;
- robust release of active touches when focus or connection state changes.

**Exit condition:** users can create and reuse mappings for different Android games without modifying MobileMapper source code.

### Phase 3 — Productization & release

Make MobileMapper practical to install and use as a normal Windows application.

Primary work:

- first-run setup and guided pairing;
- automatic device rediscovery and reconnect where feasible;
- game/app launch shortcuts where supported;
- full-screen / borderless play modes;
- latency, stability, and resource-usage tuning;
- persistent settings and profile management UX;
- structured logging and troubleshooting tools;
- automated tests for mapping/profile logic;
- packaging and a Windows installer;
- third-party license notices and release documentation.

**Exit condition:** a non-developer can install MobileMapper, pair a supported Android device, configure a profile, and use it without terminal commands.

## Architecture status

Phase 0 selected **C# / .NET 10 + WinUI 3**, an app-owned **D3D11 SwapChainPanel**
viewport, and a small **C++ / FFmpeg** media bridge. Wireless ADB manages pairing
and discovery; a custom client talks to the pinned **scrcpy 5.0.1 server**. Input
uses Win32 Raw Input and a shared touch allocator; profiles use versioned JSON
with normalized content coordinates. Initial support target: Windows 11 x64 and
Android 11+ Wireless Debugging.

The architecture is documented, but the Windows/Android critical-path acceptance
gate has **not** passed. Offline experiments are not a working desktop product.

- [Architecture and Phase 1 implementation order](docs/ARCHITECTURE.md)
- [Research, alternatives, test results and physical-device acceptance](docs/PHASE0_RESEARCH.md)
- [Third-party licensing and distribution gates](docs/THIRD_PARTY.md)
- [Official source register](docs/SOURCES.md)
- [Experimental protocol/geometry/decode lab](experiments/phase0/README.md)

UI, device lifecycle, protocol, media, PC input, mapping, profiles and diagnostics
have explicit boundaries. No external scrcpy window is reparented into the app.

## Scope boundaries

MobileMapper is intended to translate user input into ordinary Android input on the user's own device.

The project must not intentionally implement:

- game process injection;
- game memory reading/writing;
- APK patching;
- anti-cheat bypasses;
- unattended gameplay bots or autonomous play;
- credential collection.

Users are responsible for complying with the terms and policies of the games and services they use with MobileMapper.

## Current status

**Phase 1 — Wireless Mirroring MVP implementation; physical acceptance pending.**

Phase 0 research and offline experiments remain available. The repository now
contains a Windows application, native media pipeline and managed tests. Windows
verification status is recorded in [PHASE1_IMPLEMENTATION.md](docs/PHASE1_IMPLEMENTATION.md).
No installer or Phase 2 key-mapping editor is included. Wireless performance,
visible rendering and remote touch cleanup still require the device checklist.

## Repository

`darkkyoung/MobileMapper`

## Phase 1 — Wireless Mirroring developer build

The Windows solution now implements pairing/discovery, selected-device reconnect,
custom scrcpy 5.0.1 video/control transport, a C++ FFmpeg/D3D11 SwapChainPanel path,
direct mouse touch and a three-contact diagnostic. No Phase 2 mapping editor or
profiles are included. **Physical Windows/Galaxy S26 acceptance is pending.**

- [Implementation, exact Windows build procedure and verification status](docs/PHASE1_IMPLEMENTATION.md)
- [Developer artifact / Galaxy S26 test procedure and PASS/FAIL checklist](docs/PHASE1_DEVICE_TEST.md)
- [Windows CI](https://github.com/darkkyoung/MobileMapper/actions/workflows/windows-build.yml)
- [Troubleshooting](docs/TROUBLESHOOTING.md)

Use a successful current-main Windows build artifact, or run
`pwsh -File scripts/build.ps1 -Configuration Release` with the documented Windows
prerequisites. Start `artifacts/MobileMapper/MobileMapper.App.exe`. Official Android
Platform-Tools and the Microsoft Visual C++ x64 Redistributable are user-installed;
ADB is not bundled. The developer output includes corresponding FFmpeg source,
patch/build recipes, notices and an artifact hash manifest. It is not a signed
installer or a completed physical-device acceptance result.
