"""Independent wire vectors + invariants; no Android/Windows success claims."""

import io
import math
import socket
import struct
import unittest

from architecture_lab import (
    DOWN, MOVE, UP, MAX_PACKET, Packet, Session, TouchLedger, VideoReader,
    joystick, normalized_point, parse_mdns, rotate_point, touch_packet, viewport_point,
)

PREAMBLE = b"\0" + b"synthetic-device".ljust(64, b"\0") + b"h264"
SESSION = bytes.fromhex("80000000 00000500 000002d0")  # 1280 x 720


class Fragments(io.BytesIO):
    def read(self, size=-1):
        return super().read(min(size, 3))


class ProtocolTests(unittest.TestCase):
    def test_finger_wire_golden(self):
        expected = bytes.fromhex(
            "02 00 1234567887654321 00000064 000000c8 0438 0780 ffff 00000000 00000000"
        )
        self.assertEqual(touch_packet(DOWN, 0x1234567887654321, 100, 200, 1080, 1920), expected)

    def test_pressure_fixed_point(self):
        self.assertEqual(touch_packet(MOVE, 1, 1, 1, 10, 10, .5)[22:24], b"\x80\0")
        self.assertEqual(touch_packet(UP, 1, 1, 1, 10, 10)[22:24], b"\0\0")

    def test_reject_invalid_touch(self):
        for args in [(3, 1, 0, 0, 1, 1), (0, -1, 0, 0, 1, 1),
                     (0, 1, 1, 0, 1, 1), (0, 1, 0, 0, 65536, 1),
                     (0, 1, 0, 0, 1, 1, math.nan)]:
            with self.subTest(args=args), self.assertRaises(ValueError):
                touch_packet(*args)

    def test_fragmented_video_config_keyframe_rotation(self):
        data = (PREAMBLE + SESSION
                + bytes.fromhex("4000000000000000 00000002 6768")
                + bytes.fromhex("20000000000004d2 00000003 000165")
                + bytes.fromhex("80000001 000002d0 00000500"))
        reader = VideoReader(Fragments(data))
        self.assertEqual(reader.next(), Session(1280, 720, False, 1))
        self.assertEqual(reader.next(), Packet(b"gh", 0, True, False, 1))
        self.assertEqual(reader.next(), Packet(b"\0\x01e", 1234, False, True, 1))
        self.assertEqual(reader.next(), Session(720, 1280, True, 2))

    def test_socket_transport(self):
        a, b = socket.socketpair()
        with a, b:
            a.settimeout(2)
            b.settimeout(2)
            a.sendall(PREAMBLE + SESSION)
            a.shutdown(socket.SHUT_WR)
            with b.makefile("rb") as stream:
                reader = VideoReader(stream)
                self.assertEqual(reader.next().width, 1280)
                with self.assertRaises(EOFError):
                    reader.next()

    def test_all_truncation_boundaries(self):
        data = PREAMBLE + SESSION + bytes.fromhex("0000000000000001 00000003 010203")
        for end in range(len(data)):
            with self.subTest(end=end), self.assertRaises(EOFError):
                reader = VideoReader(Fragments(data[:end]))
                reader.next()
                reader.next()

    def test_bad_preamble(self):
        for data in [b"X" + PREAMBLE[1:], PREAMBLE[:-4] + b"h265"]:
            with self.assertRaises(ValueError):
                VideoReader(io.BytesIO(data))

    def test_oversized_packet(self):
        reader = VideoReader(io.BytesIO(PREAMBLE + SESSION + struct.pack(">QI", 0, MAX_PACKET + 1)))
        reader.next()
        with self.assertRaises(ValueError):
            reader.next()

    def test_media_requires_session(self):
        reader = VideoReader(io.BytesIO(PREAMBLE + struct.pack(">QI", 0, 1)))
        with self.assertRaises(ValueError):
            reader.next()

    def test_invalid_session(self):
        for header in [struct.pack(">III", 0x80000002, 1, 1), struct.pack(">III", 0x80000000, 0, 1)]:
            with self.assertRaises(ValueError):
                VideoReader(io.BytesIO(PREAMBLE + header)).next()


class MappingTests(unittest.TestCase):
    def test_normalized_edges(self):
        self.assertEqual(normalized_point(0, 0, 1920, 1080), (0, 0))
        self.assertEqual(normalized_point(1, 1, 1920, 1080), (1919, 1079))
        with self.assertRaises(ValueError):
            normalized_point(math.nan, 0, 1920, 1080)

    def test_dpi_and_letterbox(self):
        for scale in [1, 1.25, 1.5, 2]:
            self.assertEqual(viewport_point(500, 500, 1000, 1000, scale, 1920, 1080), (.5, .5))
            self.assertIsNone(viewport_point(500, 50, 1000, 1000, scale, 1920, 1080))

    def test_rotation_roundtrip(self):
        for turns in range(4):
            u, v = rotate_point(.17, .78, turns)
            u, v = rotate_point(u, v, -turns)
            self.assertAlmostEqual(u, .17)
            self.assertAlmostEqual(v, .78)

    def test_diagonal_not_faster(self):
        self.assertAlmostEqual(math.hypot(*joystick({"W", "D"})), 1)
        self.assertEqual(joystick({"W", "S"}), (0, 0))
        self.assertEqual(joystick({"W"}), (0, -1))

    def test_three_contacts_independent(self):
        ledger = TouchLedger(1920, 1080)
        packets = [ledger.down(owner, u, v) for owner, u, v in
                   [("movement", .17, .78), ("aim", .8, .7), ("gadget", .9, .9)]]
        self.assertEqual([int.from_bytes(p[2:10], "big") for p in packets], [1, 2, 3])
        self.assertEqual(ledger.up("gadget")[1], UP)
        self.assertEqual(set(ledger.active), {"movement", "aim"})
        self.assertEqual(ledger.move("movement", .2, .7, 1)[1], MOVE)

    def test_focus_release_and_stale_generation(self):
        ledger = TouchLedger(100, 100)
        ledger.down("one", .1, .2)
        ledger.down("two", .3, .4)
        releases = ledger.suspend()
        self.assertEqual([p[1] for p in releases], [UP, UP])
        self.assertEqual(ledger.active, {})
        self.assertEqual(ledger.suspend(), [])
        with self.assertRaises(ValueError):
            ledger.down("three", .1, .1)
        ledger.resume()
        ledger.down("one", .1, .1)
        with self.assertRaises(ValueError):
            ledger.move("one", .2, .2, 1)

    def test_capacity_and_duplicate_owner(self):
        ledger = TouchLedger(100, 100)
        for i in range(10):
            ledger.down(str(i), .5, .5)
        with self.assertRaises(ValueError):
            ledger.down("overflow", .5, .5)
        with self.assertRaises(ValueError):
            ledger.down("0", .5, .5)
        ledger.up("0")
        ledger.down("replacement", .5, .5)
        self.assertEqual(len(ledger.active), 10)


class DiscoveryTests(unittest.TestCase):
    def test_ports_are_distinct(self):
        services = parse_mdns("""List of discovered mdns services
example-pair _adb-tls-pairing._tcp 192.0.2.1:31001
example-connect _adb-tls-connect._tcp. 192.0.2.1:42002 device.local
legacy _adb._tcp 192.0.2.1:5555
""")
        self.assertEqual(len(services), 2)
        self.assertNotEqual(services[0].endpoint, services[1].endpoint)
        self.assertTrue(services[1].kind.startswith("_adb-tls-connect"))

    def test_refresh_replaces_stale_endpoint(self):
        old = parse_mdns("example _adb-tls-connect._tcp 192.0.2.1:42002")
        fresh = parse_mdns("example _adb-tls-connect._tcp 192.0.2.2:43003")
        self.assertNotEqual(old[0].endpoint, fresh[0].endpoint)
        self.assertEqual(parse_mdns("List of discovered mdns services"), [])

    def test_ipv6_multiple_devices_and_bad_port(self):
        services = parse_mdns("""one _adb-tls-connect._tcp [2001:db8::1]:40000
two _adb-tls-connect._tcp 192.0.2.2:40001
bad _adb-tls-connect._tcp 192.0.2.3:99999
""")
        self.assertEqual(len(services), 2)
        self.assertEqual(services[0].endpoint, "[2001:db8::1]:40000")


if __name__ == "__main__":
    unittest.main()
