# Phase 1 troubleshooting

- **Cannot start / missing native DLL:** extract the whole developer artifact;
  install official Visual C++ x64 Redistributable. Use Windows 11 x64. Do not download
  individual DLLs from third-party DLL sites. Check the successful CI commit/artifact.
- **Select adb.exe:** choose it inside the official extracted Windows Platform-Tools
  directory, with its companion DLLs. MobileMapper never searches the user's PATH.
- **Discovery empty:** verify same Wi-Fi, no guest/client isolation or VPN interference.
  Enter the main Wireless debugging connection endpoint manually. The pairing dialog
  uses a different temporary port. Phone screen prompts may require approval.
- **Pairing failed:** reopen Pair device with pairing code on the phone; use the new
  endpoint/code. The code is cleared by design. Revoked authorization needs re-pairing.
- **Private ADB daemon stopped:** restart MobileMapper; do not kill Android Studio's
  daemon. Report whether two ADB versions/daemons coexist. Check firewall prompts for
  the official adb.exe; never expose an ADB daemon to the public network.
- **Server hash mismatch:** use the complete build for the matching commit. The app
  rejects modified/mismatched scrcpy-server files. Do not disable the hash check.
- **Streaming but black/protected content:** try the Android home screen. Secure
  surfaces and OEM restrictions may prevent capture; there is no bypass feature.
- **Touch paused:** wait for a current frame and click Enable touch. Release, focus
  loss, reconnect and geometry changes intentionally pause input.
- **Repeated recovery/backlog:** check Wi-Fi interference, CPU load and power saving.
  Current baseline decodes H.264 on CPU at max 1280 / 60 FPS / 8 Mbps. D3D11VA and
  user-adjustable streaming settings are not implemented yet. Record the failure;
  do not raise queue bounds indefinitely to hide latency.
- **Presenter failed:** restart the app and check graphics drivers. D3D device-lost
  recreation is not implemented. WARP CI checks do not verify your physical GPU.
- **Hard disconnect with held Android contacts:** the PC clears local state but
  cannot guarantee delivery of remote UP over a dead connection. Record this as a
  physical acceptance failure. A future server watchdog decision depends on evidence.

Logs are bounded and in memory. Do not paste private keys, pairing codes, credential
files or unrelated phone content into an issue.
