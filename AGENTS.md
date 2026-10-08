# AGENTS.md

This file defines repository-level instructions for AI coding agents working on MobileMapper.

## 1. Read first

Before making changes:

1. Read `README.md`.
2. Inspect the current repository state and relevant existing code.
3. Treat the current remote `main` branch as the source of truth.
4. Do not assume work from a previous run was completed unless it is present in the repository.
5. Stay within the task requested by the user.

If the requested change conflicts with this file or with an already-documented architectural decision, explain the conflict before changing direction.

## 2. Project intent

MobileMapper is a Windows desktop application that connects to a real Android device over a local wireless connection, mirrors its screen, and maps PC keyboard/mouse input to ordinary Android touch input.

The project is:

- wireless-first;
- game-agnostic;
- profile-based;
- intended for interactive, user-driven control;
- not an Android emulator.

Do not hard-code the architecture around one game.

## 3. Architecture discipline

Keep these concerns separable:

- desktop UI;
- Android device discovery / pairing / connection;
- screen transport / decoding / rendering;
- PC input capture;
- mapping engine;
- Android input delivery;
- profiles / settings;
- diagnostics / logging.

Do not collapse unrelated layers merely to make an early prototype shorter.

At the same time, avoid speculative abstractions that have no current use. Prefer the smallest design that preserves clear boundaries.

## 4. Phase policy

The roadmap in `README.md` uses a small number of large phases.

### Phase 0 — Foundation & architecture

Do not rush into the production implementation.

Before locking the stack, investigate and validate:

- Windows desktop framework options;
- ADB / Wireless Debugging integration;
- mDNS discovery and reconnect behavior;
- scrcpy integration or interoperability options;
- screen transport, decoding, rendering, and latency;
- Android input injection and concurrent touch;
- profile coordinate representation;
- packaging and third-party licensing implications.

Record decisions and important trade-offs in repository documentation.

A proof of concept is allowed when it is needed to validate a risky technical assumption, but do not mistake a throwaway prototype for the final architecture.

Phase 0 decisions are now recorded in `docs/ARCHITECTURE.md`; read it and
`docs/PHASE0_RESEARCH.md` before implementation. Follow the selected WinUI 3 / .NET,
native FFmpeg / D3D11, pinned scrcpy-server architecture unless new evidence requires
an explicitly documented revision. `experiments/phase0` is an offline executable
specification, not production code. Windows/Android acceptance and artifact-specific
distribution gates remain open; passing offline tests does not close them.

### Later phases

Once Phase 0 decisions are documented, implement according to the roadmap in `README.md`.

Do not silently skip phase exit conditions.

## 5. Key-mapping principles

The mapping system must be general-purpose.

Expected primitives include:

- Tap;
- Hold;
- Joystick;
- Mouse Aim;
- Swipe / Drag;
- required multi-touch coordination.

Mappings should use a coordinate representation that is resilient to desktop window resizing and suitable Android resolution changes.

Profiles must remain independent of a specific game whenever possible.

Input-state handling must fail safely. On focus loss, disconnect, mapping-mode exit, or abnormal shutdown paths, active synthetic touches/holds should be released so the Android device is not left with stuck input state.

## 6. Security and fair-use boundaries

Do not implement or add:

- game process injection;
- memory scanning, reading, or writing;
- APK patching;
- anti-cheat bypasses;
- credential harvesting;
- hidden persistence;
- unattended gameplay bots or autonomous play.

MobileMapper should translate live user input to ordinary Android input. It should not modify the game process itself.

Do not commit device credentials, pairing secrets, private keys, tokens, or personal identifiers.

## 7. Third-party code and licensing

Before vendoring, copying, modifying, or redistributing third-party components:

- identify the license;
- verify that the planned integration and redistribution are compatible;
- preserve required copyright/license notices;
- document runtime or source distribution obligations.

Do not copy substantial third-party source into the repository merely for convenience when a cleaner dependency/integration model is available.

If scrcpy, ADB/platform-tools, FFmpeg, or another substantial component is selected, document the exact integration strategy and license implications before packaging it.

## 8. Change discipline

For every task:

- inspect before editing;
- keep the diff focused;
- do not perform unrelated refactors;
- do not rename public concepts without a reason;
- preserve working behavior outside the requested scope;
- update documentation when behavior or architecture materially changes.

Do not claim something works unless it was actually validated to the extent possible in the current environment.

If validation cannot be performed, state exactly what remains unverified.

## 9. Build and test discipline

Once executable code exists:

- run the relevant formatter/linter if the project defines one;
- build the affected target;
- run relevant automated tests;
- add or update tests for non-trivial mapping/profile logic when practical.

For latency-sensitive or device-dependent behavior, distinguish between:

- unit/integration validation performed locally;
- behavior that still requires a real Android device;
- behavior that still requires Windows-specific manual testing.

Never hide failing tests or suppress meaningful errors simply to produce a green build.

## 10. Repository hygiene

Do not commit:

- build outputs;
- installer artifacts unless the repository later defines a release-artifact policy;
- local IDE state;
- logs;
- crash dumps;
- ADB keys;
- pairing secrets;
- `.env` files containing secrets;
- temporary captures or user profile data.

Keep generated dependencies and large binaries out of Git unless an explicit repository decision says otherwise.

## 11. Commit quality

Use concise commit messages that describe the actual change.

Examples:

- `docs: document Phase 0 architecture decision`
- `feat: add wireless device discovery`
- `feat: add normalized tap mapping`
- `fix: release active touches on focus loss`
- `test: cover profile coordinate conversion`

Before committing, review the final diff for accidental files and out-of-scope changes.

## 12. Definition of done

A task is not complete merely because code was written.

A completed task should include, as applicable:

- requested behavior implemented;
- relevant build/tests passing;
- important manual validation performed or clearly marked as pending;
- documentation updated;
- no unrelated regressions introduced;
- final report containing what changed, how it was validated, and any remaining risk.
