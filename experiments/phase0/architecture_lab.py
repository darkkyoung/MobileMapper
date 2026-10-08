"""Experimental executable contracts; NOT a production client or device test.

Wire reference: scrcpy v5.0.1, a60891aea193d92e7e5c3942700eca63f9d19a5f.
Original implementation from protocol facts; no upstream source is vendored.
"""

import math
import struct
from dataclasses import dataclass

SERVER_VERSION = "5.0.1"
MAX_PACKET = 16 * 1024 * 1024  # Local defensive policy, not an upstream limit.
DOWN, UP, MOVE = 0, 1, 2


def read_exact(stream, size):
    data = bytearray()
    while len(data) < size:
        part = stream.read(size - len(data))
        if not part:
            raise EOFError(f"truncated stream: expected {size}, received {len(data)}")
        data.extend(part)
    return bytes(data)


@dataclass(frozen=True)
class Session:
    width: int
    height: int
    client_resized: bool
    generation: int


@dataclass(frozen=True)
class Packet:
    payload: bytes
    pts_us: int
    config: bool
    keyframe: bool
    generation: int


class VideoReader:
    """Only video+control, audio=false, forward tunnel, default metadata flags."""

    def __init__(self, stream):
        self.stream = stream
        self.generation = 0
        if read_exact(stream, 1) != b"\0":
            raise ValueError("invalid forward readiness byte")
        self.device_name = read_exact(stream, 64).split(b"\0", 1)[0].decode(
            "utf-8", errors="replace"
        )
        if read_exact(stream, 4) != b"h264":
            raise ValueError("experiment only supports H.264")

    def next(self):
        header = read_exact(self.stream, 12)
        if header[0] & 0x80:
            flags, width, height = struct.unpack(">III", header)
            if flags & ~0x80000001 or not (0 < width <= 65535 and 0 < height <= 65535):
                raise ValueError("unsupported session metadata")
            self.generation += 1
            return Session(width, height, bool(flags & 1), self.generation)
        flags_pts, size = struct.unpack(">QI", header)
        if not self.generation or not 0 < size <= MAX_PACKET:
            raise ValueError("missing session or invalid packet size")
        return Packet(
            read_exact(self.stream, size),
            flags_pts & ((1 << 61) - 1),
            bool(flags_pts & (1 << 62)),
            bool(flags_pts & (1 << 61)),
            self.generation,
        )


def touch_packet(action, pointer, x, y, width, height, pressure=None):
    """32 bytes, big endian; ordinary finger IDs, no mouse-button semantics."""
    if action not in (DOWN, UP, MOVE) or not 0 <= pointer < (1 << 63):
        raise ValueError("invalid action or reserved pointer ID")
    if not (0 < width <= 65535 and 0 < height <= 65535):
        raise ValueError("invalid dimensions")
    if not (0 <= x < width and 0 <= y < height):
        raise ValueError("point outside video")
    pressure = (0.0 if action == UP else 1.0) if pressure is None else pressure
    if not math.isfinite(pressure) or not 0 <= pressure <= 1:
        raise ValueError("invalid pressure")
    fixed = min(65535, int(pressure * 65536))
    return struct.pack(">BBQiiHHHII", 2, action, pointer, x, y, width, height, fixed, 0, 0)


def normalized_point(u, v, width, height):
    if not all(math.isfinite(a) and 0 <= a <= 1 for a in (u, v)):
        raise ValueError("normalized point outside [0, 1]")
    if width <= 0 or height <= 0:
        raise ValueError("invalid dimensions")
    return min(width - 1, int(u * width)), min(height - 1, int(v * height))


def viewport_point(x_dip, y_dip, panel_width_dip, panel_height_dip, scale, width, height):
    """DIP -> physical panel pixels -> letterboxed video -> normalized point."""
    if min(panel_width_dip, panel_height_dip, scale, width, height) <= 0:
        raise ValueError("invalid viewport")
    x, y = x_dip * scale, y_dip * scale
    pw, ph = panel_width_dip * scale, panel_height_dip * scale
    fit = min(pw / width, ph / height)
    vw, vh = width * fit, height * fit
    left, top = (pw - vw) / 2, (ph - vh) / 2
    if not (left <= x < left + vw and top <= y < top + vh):
        return None  # Letterbox clicks never become edge taps.
    return (x - left) / vw, (y - top) / vh


def rotate_point(u, v, clockwise_quarters):
    """Optional profile transform, not a second transform on scrcpy video input."""
    for _ in range(clockwise_quarters % 4):
        u, v = 1 - v, u
    return u, v


def joystick(keys):
    x = int("D" in keys) - int("A" in keys)
    y = int("S" in keys) - int("W" in keys)
    length = max(1.0, math.hypot(x, y))
    return x / length, y / length


class TouchLedger:
    """Local intent model only. Emitted UP is NOT proof of remote release."""

    def __init__(self, width, height):
        self.width, self.height = width, height
        self.active = {}
        self.generation = 1
        self.enabled = True
        self.next_id = 1

    def down(self, owner, u, v):
        if not self.enabled or owner in self.active or len(self.active) >= 10:
            raise ValueError("suspended, duplicate owner, or pointer capacity exceeded")
        x, y = normalized_point(u, v, self.width, self.height)
        pointer = self.next_id
        packet = touch_packet(DOWN, pointer, x, y, self.width, self.height)
        self.next_id += 1
        self.active[owner] = (pointer, x, y)
        return packet

    def move(self, owner, u, v, generation):
        if not self.enabled or generation != self.generation:
            raise ValueError("stale generation or suspended")
        pointer, _, _ = self.active[owner]
        x, y = normalized_point(u, v, self.width, self.height)
        self.active[owner] = (pointer, x, y)
        return touch_packet(MOVE, pointer, x, y, self.width, self.height)

    def up(self, owner):
        pointer, x, y = self.active.pop(owner)
        return touch_packet(UP, pointer, x, y, self.width, self.height)

    def suspend(self):
        self.enabled = False
        packets = [self.up(owner) for owner in list(self.active)]
        self.generation += 1
        return packets

    def resume(self):
        # Caller must first observe neutral physical keys/buttons and a healthy session.
        self.enabled = True


@dataclass(frozen=True)
class AdbService:
    name: str
    kind: str
    endpoint: str


def parse_mdns(output):
    """Minimal known adb format, tolerates extra columns; no discovery occurs here."""
    services = []
    for line in output.splitlines():
        fields = line.split()
        if len(fields) < 3:
            continue
        name, kind, endpoint = fields[:3]
        kind = kind.rstrip(".")
        if kind not in ("_adb-tls-pairing._tcp", "_adb-tls-connect._tcp"):
            continue
        host, separator, port = endpoint.rpartition(":")
        if not separator or not host or not port.isdecimal() or not 0 < int(port) < 65536:
            continue
        services.append(AdbService(name, kind, endpoint))
    return services
