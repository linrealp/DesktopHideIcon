#!/usr/bin/env python3
"""Generate the small multi-resolution application icon using only stdlib.

Run from anywhere:
    python tools/make_icon.py
Optional preview:
    python tools/make_icon.py --preview assets/app-preview.png
"""

import math
import os
import struct
import sys
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ICON_PATH = os.path.join(ROOT, "assets", "app.ico")
PREVIEW_PATH = os.path.join(ROOT, "assets", "app-preview.png")
SIZES = (16, 32, 48)
SUPERSAMPLE = 4


def clamp(value, low, high):
    return max(low, min(high, value))


def rounded_rect_inside(px, py, x0, y0, x1, y1, radius):
    if px < x0 or py < y0 or px > x1 or py > y1:
        return False
    cx = clamp(px, x0 + radius, x1 - radius)
    cy = clamp(py, y0 + radius, y1 - radius)
    dx = px - cx
    dy = py - cy
    return dx * dx + dy * dy <= radius * radius


def mix(a, b, t):
    return (
        a[0] + (b[0] - a[0]) * t,
        a[1] + (b[1] - a[1]) * t,
        a[2] + (b[2] - a[2]) * t,
    )


def sample_color(px, py):
    """Return RGBA values in 0..1 at a normalized icon coordinate."""
    # Rounded blue background with a subtle vertical gradient.
    if not rounded_rect_inside(px, py, 0.02, 0.02, 0.98, 0.98, 0.22):
        return (0.0, 0.0, 0.0, 0.0)

    top = (0.196, 0.431, 1.0)
    bottom = (0.055, 0.184, 0.553)
    bg = mix(top, bottom, clamp((py - 0.02) / 0.96, 0.0, 1.0))

    # A soft highlight across the upper part of the tile.
    if py < 0.42:
        bg = mix(bg, (1.0, 1.0, 1.0), 0.06 * (1.0 - py / 0.42))

    white = (0.965, 0.980, 1.0)

    # Desktop monitor frame.
    outside_monitor = rounded_rect_inside(px, py, 0.16, 0.17, 0.84, 0.595, 0.065)
    inside_screen = rounded_rect_inside(px, py, 0.205, 0.215, 0.795, 0.545, 0.038)

    if outside_monitor and not inside_screen:
        return (white[0], white[1], white[2], 1.0)

    # A 2x2 grid of desktop icon tiles.
    if inside_screen:
        tiles = (
            (0.255, 0.265, 0.440, 0.375),
            (0.560, 0.265, 0.745, 0.375),
            (0.255, 0.405, 0.440, 0.505),
            (0.560, 0.405, 0.745, 0.505),
        )
        for index, tile in enumerate(tiles):
            if rounded_rect_inside(px, py, tile[0], tile[1], tile[2], tile[3], 0.020):
                # Slightly dimmer lower row to keep a little depth.
                shade = 1.0 if index < 2 else 0.88
                return (white[0] * shade, white[1] * shade, white[2] * shade, 1.0)

    # Monitor neck and base.
    if rounded_rect_inside(px, py, 0.445, 0.585, 0.555, 0.685, 0.012):
        return (white[0], white[1], white[2], 1.0)
    if rounded_rect_inside(px, py, 0.335, 0.675, 0.665, 0.735, 0.030):
        return (white[0], white[1], white[2], 1.0)

    return (bg[0], bg[1], bg[2], 1.0)


def render_rgba(size):
    pixels = bytearray(size * size * 4)
    step = 1.0 / (size * SUPERSAMPLE)

    for y in range(size):
        for x in range(size):
            r_sum = 0.0
            g_sum = 0.0
            b_sum = 0.0
            a_sum = 0.0
            samples = SUPERSAMPLE * SUPERSAMPLE

            for sy in range(SUPERSAMPLE):
                py = (y * SUPERSAMPLE + sy + 0.5) * step
                for sx in range(SUPERSAMPLE):
                    px = (x * SUPERSAMPLE + sx + 0.5) * step
                    r, g, b, a = sample_color(px, py)
                    r_sum += r * a
                    g_sum += g * a
                    b_sum += b * a
                    a_sum += a

            alpha = a_sum / samples
            if alpha > 0.0:
                red = r_sum / a_sum
                green = g_sum / a_sum
                blue = b_sum / a_sum
            else:
                red = green = blue = 0.0

            offset = (y * size + x) * 4
            pixels[offset] = int(round(clamp(red, 0.0, 1.0) * 255))
            pixels[offset + 1] = int(round(clamp(green, 0.0, 1.0) * 255))
            pixels[offset + 2] = int(round(clamp(blue, 0.0, 1.0) * 255))
            pixels[offset + 3] = int(round(clamp(alpha, 0.0, 1.0) * 255))

    return pixels


def encode_ico_image(size, rgba):
    xor = bytearray()
    for y in range(size - 1, -1, -1):
        row_start = y * size * 4
        for x in range(size):
            offset = row_start + x * 4
            red = rgba[offset]
            green = rgba[offset + 1]
            blue = rgba[offset + 2]
            alpha = rgba[offset + 3]
            xor.extend((blue, green, red, alpha))

    mask_row_bytes = ((size + 31) // 32) * 4
    mask = bytes(mask_row_bytes * size)

    header = struct.pack(
        "<IiiHHIIiiII",
        40,
        size,
        size * 2,
        1,
        32,
        0,
        len(xor) + len(mask),
        0,
        0,
        0,
        0,
    )

    return header + bytes(xor) + mask


def write_ico(path, images):
    count = len(images)
    offset = 6 + count * 16
    entries = bytearray()
    payload = bytearray()

    for size, data in images:
        entries.extend(
            struct.pack(
                "<BBBBHHII",
                0 if size >= 256 else size,
                0 if size >= 256 else size,
                0,
                0,
                1,
                32,
                len(data),
                offset,
            )
        )
        payload.extend(data)
        offset += len(data)

    with open(path, "wb") as handle:
        handle.write(struct.pack("<HHH", 0, 1, count))
        handle.write(entries)
        handle.write(payload)


def write_png(path, size, rgba):
    raw = bytearray()
    for y in range(size):
        raw.append(0)
        raw.extend(rgba[y * size * 4 : (y + 1) * size * 4])

    def chunk(kind, data):
        body = kind + data
        return (
            struct.pack(">I", len(data))
            + body
            + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)
        )

    png = bytearray()
    png.extend(b"\x89PNG\r\n\x1a\n")
    png.extend(chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)))
    png.extend(chunk(b"IDAT", zlib.compress(bytes(raw), 9)))
    png.extend(chunk(b"IEND", b""))

    with open(path, "wb") as handle:
        handle.write(png)


def main():
    os.makedirs(os.path.dirname(ICON_PATH), exist_ok=True)
    images = []
    for size in SIZES:
        images.append((size, encode_ico_image(size, render_rgba(size))))

    write_ico(ICON_PATH, images)
    print("Wrote %s (%d bytes)" % (ICON_PATH, os.path.getsize(ICON_PATH)))

    if "--preview" in sys.argv:
        preview = render_rgba(128)
        write_png(PREVIEW_PATH, 128, preview)
        print("Wrote %s" % PREVIEW_PATH)


if __name__ == "__main__":
    main()
