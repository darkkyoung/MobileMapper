"""Synthetic scrcpy framing -> socket -> demux -> real FFmpeg software decode.

Requires a developer FFmpeg with libx264 solely to generate fixtures in memory.
It is NOT a selected distribution binary, Android encoder, or latency benchmark.
"""

import io
import re
import socket
import struct
import subprocess
import threading

from architecture_lab import Packet, Session, VideoReader


def ffmpeg(args, data=None):
    return subprocess.run(
        ["ffmpeg", "-hide_banner", "-loglevel", "error", *args], input=data,
        capture_output=True, check=True, timeout=30,
    ).stdout


def encode(width, height):
    return ffmpeg([
        "-f", "lavfi", "-i", f"testsrc2=size={width}x{height}:rate=60",
        "-frames:v", "12", "-c:v", "libx264", "-threads", "1",
        "-preset", "ultrafast", "-tune", "zerolatency",
        "-x264-params", "aud=1:keyint=6", "-f", "h264", "pipe:1",
    ])


def frame_crc(data):
    decoded = ffmpeg(["-f", "h264", "-i", "pipe:0", "-threads", "1", "-f", "framecrc", "pipe:1"], data)
    frames = [line for line in decoded.splitlines() if line and not line.startswith(b"#")]
    if len(frames) != 12:
        raise AssertionError(f"expected 12 decoded frames, received {len(frames)}")
    return decoded


def main():
    sequences = [(160, 90), (90, 160)]
    originals = [encode(w, h) for w, h in sequences]
    framed = bytearray(b"\0" + b"synthetic".ljust(64, b"\0") + b"h264")
    for (width, height), data in zip(sequences, originals):
        framed.extend(struct.pack(">III", 0x80000000, width, height))
        # The fixture encoder emits Access Unit Delimiters, one per picture.
        offsets = [m.start() for m in re.finditer(b"\x00\x00\x00\x01\x09", data)]
        if len(offsets) != 12 or offsets[0] != 0:
            raise AssertionError("fixture encoder did not emit expected AUDs")
        offsets.append(len(data))
        for i, (start, end) in enumerate(zip(offsets, offsets[1:])):
            payload = data[start:end]
            flags = (1 << 61) if i % 6 == 0 else 0
            framed.extend(struct.pack(">QI", flags | (i * 16667), len(payload)))
            framed.extend(payload)

    failures = []
    sender, receiver = socket.socketpair()
    sender.settimeout(5)
    receiver.settimeout(5)

    def send():
        try:
            with sender:
                for offset in range(0, len(framed), 137):
                    sender.sendall(framed[offset:offset + 137])
                sender.shutdown(socket.SHUT_WR)
        except Exception as exc:
            failures.append(exc)

    thread = threading.Thread(target=send)
    thread.start()
    received = []
    try:
        with receiver, receiver.makefile("rb") as stream:
            reader = VideoReader(stream)
            for index, (width, height) in enumerate(sequences, 1):
                session = reader.next()
                if session != Session(width, height, False, index):
                    raise AssertionError(session)
                payloads = io.BytesIO()
                for _ in range(12):
                    packet = reader.next()
                    if not isinstance(packet, Packet) or packet.generation != index:
                        raise AssertionError(packet)
                    payloads.write(packet.payload)
                received.append(payloads.getvalue())
    finally:
        thread.join(timeout=6)
    if thread.is_alive() or failures:
        raise AssertionError(f"socket sender failed: {failures}")
    for index, (original, rebuilt) in enumerate(zip(originals, received), 1):
        if rebuilt != original or frame_crc(rebuilt) != frame_crc(original):
            raise AssertionError("payload or decoded frame checksums differ")
        print(f"PASS session {index}: {sequences[index - 1]}, 12 frames, identical frame CRCs")
    print("PASS: 24 frames across 2 synthetic sessions; no device/render/latency claim")


if __name__ == "__main__":
    main()
