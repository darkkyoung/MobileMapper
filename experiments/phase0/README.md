# Phase 0 architecture lab — EXPERIMENTAL

This is a disposable executable specification, not MobileMapper's production
client or UI. The selected product stack remains C#/.NET + WinUI 3 with a C++
media bridge. No Android device is contacted and no real input is injected.

## Why these experiments exist

- Reject reliance on obsolete scrcpy stream headers: test the session/media format
  inspected at server 5.0.1, commit `a60891aea193d92e7e5c3942700eca63f9d19a5f`.
- Check arbitrary socket fragments, truncation, length caps and touch wire layout.
- Exercise shared pointer ownership, focus barriers, geometry and port separation
  before committing to a full product implementation.
- Test actual software decode after synthetic framing/network/demux processing.

See [source register](../../docs/SOURCES.md) for the exact upstream implementation
references. Code here is original; no third-party implementation was vendored.

## Run from repository root

Python 3.10+ standard library is enough for the unit suite (tested on 3.12.14):

```sh
python3 -m unittest discover -s experiments/phase0 -v
```

On Windows with Python installed, use `py -3` in place of `python3`.

The optional decode probe also requires a developer FFmpeg in PATH with a libx264
encoder. The system test environment had FFmpeg 6.1.1 with GPL enabled. That binary
is **not** the intended LGPL-only product dependency and is not distributed here.

```sh
python3 experiments/phase0/decode_probe.py
```

The probe generates two 12-frame H.264 sequences in memory, packs AUD-separated
access units into synthetic scrcpy records, sends them through a socketpair in
small chunks, parses session changes, and decodes the reconstructed streams.
It compares payloads and every decoded frame CRC with direct decoding. It creates
no capture files. Each subprocess/socket operation is bounded by a timeout.

Recorded results, 2026-10-08: **20 tests passed**; decode probe **24 frames / 2
sessions**, matching CRCs. It intentionally does not require .NET, ADB, Windows
or Android, so passing it cannot certify any of those systems.

## What is still unproved

No real mDNS/TLS pairing/reboot/reconnect, no real scrcpy stream, no native AVCodec
adapter, no Windows build, no D3D11VA/render/WinUI overlay, no hardware latency,
and no Android multi-touch acceptance/release were tested. Discovery outputs and
device names are synthetic. Parser support for IPv6 examples is not a claim that
all ADB versions emit that format. Local UP packet generation is not a guarantee
that Android receives or honors it.

The decoder probe uses in-band codec config; separate config packets are tested
only at parser level. It decodes each synthetic session separately, modeling a
decoder reset but not proving live decoder reconfiguration. The simple TouchLedger
does not implement asynchronous network acknowledgment, timer scheduling, queue
coalescing or a full mapping engine. Port production logic only with the relevant
Phase 1/2 tests; do not import this experiment into the shipped app.

Follow the hardware acceptance matrix in
[PHASE0_RESEARCH.md](../../docs/PHASE0_RESEARCH.md) before expanding implementation.
