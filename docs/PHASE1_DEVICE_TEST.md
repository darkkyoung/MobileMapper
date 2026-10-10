# Phase 1 — Windows 11 / Galaxy S26 physical acceptance

**All physical results are NOT TESTED until a person records them below.** CI cannot
verify the phone, Wi-Fi, visible WinUI window or Android input behavior.

## Get the developer build

1. Open the repository's [Windows build workflow](https://github.com/darkkyoung/MobileMapper/actions/workflows/windows-build.yml).
2. Choose a **successful run for the current main commit**. Download
   `MobileMapper-win-x64-developer` from Artifacts (GitHub sign-in may be needed).
   Extract the entire ZIP. Keep the DLLs and `third-party` folder beside the EXE.
   If no successful artifact exists, use the exact local Windows build procedure
   in [PHASE1_IMPLEMENTATION.md](PHASE1_IMPLEMENTATION.md); do not use a failed run.
3. Install the [Microsoft Visual C++ x64 Redistributable](https://aka.ms/vc14/vc_redist.x64.exe)
   if it is not already installed. .NET and Windows App SDK are self-contained in
   the developer output. This build is unsigned; check the repo/run and downloaded
   artifact before deciding whether to run it. Do not disable Windows protection.
4. Download **SDK Platform-Tools for Windows** from
   <https://developer.android.com/tools/releases/platform-tools>, accept Google's
   terms and extract the entire folder. Do not move adb.exe away from its DLLs.
   ADB is deliberately not bundled by MobileMapper.
5. Read `READ-ME-FIRST.txt` and applicable third-party licenses in the extracted
   build. Double-click **MobileMapper.App.exe**. No terminal is needed to operate it.

## Prepare the phone

Connect Windows and the real Galaxy S26 to the same trusted Wi-Fi/local network.
On Samsung Settings, enable Developer options (About phone → Software information
→ tap Build number seven times if needed), then enable **Wireless debugging** in
Developer options. Menu wording can vary with One UI/Android version. Keep the phone
unlocked. No USB cable, companion application or APK modification is required.

For touch diagnostics, use a safe Android multi-touch test screen that displays
independent contacts; Samsung Developer options' **Pointer location** can help show
coordinates/count. Do not test on purchase, account deletion or other sensitive UI.
Record the test app/screen used. MobileMapper does not install a touch-test APK.

## Exact MobileMapper controls

1. Click **Browse adb.exe**, choose adb.exe in the official Platform-Tools folder.
2. Click **Refresh discovery / devices**. Discovery may show separate pairing and
   connection services. Selecting one fills only its corresponding endpoint field.
3. On the phone choose **Pair device with pairing code**. In MobileMapper enter
   its **Pairing endpoint (IP:port)** and **6-digit pairing code**, then click
   **Pair device**. The code box clears immediately. Keep the phone dialog open
   until the result appears. This port is temporary and is not the connection port.
4. Return to the phone's main Wireless debugging screen. Click **Refresh discovery /
   devices**. If the phone does not appear, enter the current main-screen IP:port in
   **Connection endpoint (IP:port)** and click **Connect endpoint**.
5. In **Select an online device**, explicitly choose your phone, then click
   **Start mirroring selected device**. Wait for `Streaming`, a nonzero resolution
   and visible moving phone content. Check that it is the selected phone.
6. Click **Enable touch**, then click a harmless point inside the image. Verify a
   real phone tap. Press and hold, drag within the image and release: verify the
   phone sees a continuous touch, not repeated independent taps. Black bars should
   never inject a new touch. Moving outside then releasing must release the contact.
7. Diagnostic test: click **Enable touch** if input is paused, then **A Down**,
   **B Down**, **C Down** (buttons are grouped below the viewport). They correspond
   to x=25%/50%/75%, y=50%. Verify **three simultaneous independent contacts** on the
   phone. Click each **Move** (y=60%), then each **Up**. Counts should return to zero.
   Repeated Down on an already held contact is rejected. **Release all (Esc)** clears
   every contact and pauses input. This is a developer diagnostic, not a key mapper.
8. Hold contacts and test **Escape**, Alt+Tab, minimize, **Disconnect / cancel
   recovery**, app close and phone rotation. Local active contacts must become zero;
   touch must remain paused until explicitly enabled again. Rotation during a held
   contact restarts the stream. Rotation without a hold should update aspect/geometry.
9. Reconnect normally: click **Refresh discovery / devices**, select the same phone,
   start mirroring and Enable touch. No previous touch may resume automatically.
10. Test Wi-Fi loss / Wireless debugging OFF. Expect recovery/error, not a frozen
    UI or infinite pending queue. Turn it back ON; ports may change. Recovery tries
    fresh discovery at most five times, then requests user action. A manual
    **Disconnect / cancel recovery** must stop retries. If the phone shows a stuck
    remote contact after a hard link loss, record FAIL, stop the test and restore
    ordinary phone input (restart the phone if necessary); do not mark this passed
    merely because the PC's contact count is zero.
11. Close/reopen MobileMapper. ADB path/identity hint are saved. Click Refresh; the
    known online phone should be selected when identifiable. Pairing should usually
    persist, but revocation/reboot/OEM behavior may require Pair device again. Never
    assume an old IP:port still belongs to the same device.

## Record results

Record Windows build, GPU/driver, display scaling, Android/One UI build, official
Platform-Tools version, Wi-Fi topology and tested MobileMapper commit. Do not attach
pairing codes, ADB keys, credentials, sensitive screenshots or unrelated user content.
Replace NOT TESTED with PASS or FAIL and add evidence/observations.

| Check | Result | Observation |
| --- | --- | --- |
| EXE launches on Windows 11 x64; native DLLs load | NOT TESTED | |
| Pairing code flow without terminal/USB | NOT TESTED | |
| Wrong code / expired pairing port gives useful failure | NOT TESTED | |
| mDNS discovery; manual connection fallback | NOT TESTED | |
| Explicit selection with two available phones | NOT TESTED | |
| Correct phone image inside MobileMapper (no external window) | NOT TESTED | |
| Motion, resolution and FPS counters update | NOT TESTED | |
| Click/press/drag match phone coordinates | NOT TESTED | |
| Letterbox does not inject; outside release is safe | NOT TESTED | |
| Resize and 100%/150%/200% Windows DPI, second monitor | NOT TESTED | |
| Portrait/landscape geometry and first new-frame input gate | NOT TESTED | |
| A+B+C held concurrently; each move/up independent | NOT TESTED | |
| Escape / Release all: phone and local contacts return to zero | NOT TESTED | |
| Viewport focus/capture loss; Alt+Tab; minimize release | NOT TESTED | |
| Disconnect and app shutdown release held contacts | NOT TESTED | |
| Hard network/control failure: local clear, remote behavior documented | NOT TESTED | |
| No stale contact replay after reconnect/session change | NOT TESTED | |
| Wireless debugging toggle/reboot/changed port reconnect | NOT TESTED | |
| Disconnect stops automatic retry | NOT TESTED | |
| Android Studio ADB daemon remains usable | NOT TESTED | |
| 15-minute stream: bounded memory/queue, no UI freeze | NOT TESTED | |
| Glass-to-glass latency (external camera method, separate from FPS) | NOT TESTED | Record p50/p95; no assumed number |

Phase 0 targets remain targets, not measured results. Physical acceptance is a gate
for Phase 2. Report FAIL with the commit, state, last safe error and reproduction.
