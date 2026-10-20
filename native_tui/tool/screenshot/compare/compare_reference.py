"""Compare captured terminal cells and rasterize both with the same font.

This is a local reference-region comparison, not a full application screenshot claim.
"""
import argparse
import json
import re
import unicodedata
from pathlib import Path
from PIL import Image, ImageChops, ImageDraw, ImageFont

WHITE, BLACK = (255, 255, 255), (0, 0, 0)
PALETTE = [(0, 0, 0), (128, 0, 0), (0, 128, 0), (128, 128, 0), (0, 0, 128), (128, 0, 128), (0, 128, 128), (192, 192, 192),
           (128, 128, 128), (255, 0, 0), (0, 255, 0), (255, 255, 0), (0, 0, 255), (255, 0, 255), (0, 255, 255), (255, 255, 255)]
TOKEN = re.compile(r'\x1b\[[0-?]*[ -/]*[@-~]|\x1b\][^\x07]*(?:\x07|\x1b\\)|[^\x1b]', re.S)


def cells(text, width=80):
    rows, x, y = [], 0, 0
    fg, bg = WHITE, BLACK
    for token in TOKEN.findall(text):
        while len(rows) <= y:
            rows.append([(' ', WHITE, BLACK) for _ in range(width)])
        if token.startswith('\x1b['):
            if token.endswith('m'):
                codes = [int(code or 0) for code in token[2:-1].split(';')]
                i = 0
                while i < len(codes):
                    code = codes[i]
                    if code == 0:
                        fg, bg = WHITE, BLACK
                    elif code == 39:
                        fg = WHITE
                    elif code == 49:
                        bg = BLACK
                    elif code in (38, 48) and i + 4 < len(codes) and codes[i + 1] == 2:
                        color = tuple(codes[i + 2:i + 5])
                        if code == 38:
                            fg = color
                        else:
                            bg = color
                        i += 4
                    elif 30 <= code <= 37:
                        fg = PALETTE[code - 30]
                    elif 90 <= code <= 97:
                        fg = PALETTE[code - 90 + 8]
                    elif 40 <= code <= 47:
                        bg = PALETTE[code - 40]
                    elif 100 <= code <= 107:
                        bg = PALETTE[code - 100 + 8]
                    i += 1
            elif token.endswith(('H', 'f')):
                coordinates = [int(code or 1) for code in token[2:-1].split(';')]
                y, x = coordinates[0] - 1, (coordinates[1] if len(coordinates) > 1 else 1) - 1
            continue
        if token.startswith('\x1b'):
            continue
        if token == '\n':
            y, x = y + 1, 0
            continue
        if token == '\r':
            x = 0
            continue
        if 0 <= x < width:
            rows[y][x] = (token, fg, bg)
        x += 2 if unicodedata.east_asian_width(token) in 'WF' else 1
    return rows


def raster(rows, font):
    cell_width, cell_height = round(font.getlength('M')), 28
    image = Image.new('RGB', (len(rows[0]) * cell_width, len(rows) * cell_height), BLACK)
    draw = ImageDraw.Draw(image)
    for y, row in enumerate(rows):
        for x, (character, fg, bg) in enumerate(row):
            left, top = x * cell_width, y * cell_height
            draw.rectangle((left, top, left + cell_width - 1, top + cell_height - 1), fill=bg)
            draw.text((left, top), character, font=font, fill=fg)
    return image


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--reference', type=Path, required=True, help='directory with reference_clawd.ansi and reference_prompt.ansi')
    parser.add_argument('--capture', type=Path, required=True, help='C# driver ANSI capture')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--font', type=Path, default=Path('C:/Windows/Fonts/CascadiaMono.ttf'))
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    actual = cells(args.capture.read_text(encoding='utf-8-sig'))
    reference_logo = cells((args.reference / 'reference_clawd.ansi').read_text(encoding='utf-8-sig'))
    reference_prompt = cells((args.reference / 'reference_prompt.ansi').read_text(encoding='utf-8-sig'))
    prompt_y = next(index for index, row in enumerate(actual) if ''.join(cell[0] for cell in row).startswith('─' * 80))
    regions = {'clawd': ([row[:9] for row in reference_logo[:3]], [row[:9] for row in actual[:3]]),
               'prompt': (reference_prompt[:3], actual[prompt_y:prompt_y + 3])}
    font = ImageFont.truetype(str(args.font), 20)
    report = {'scope': 'Clawd source + PromptInput border properties; same-font raster of captured cells', 'font': str(args.font), 'regions': {}}
    for name, (expected, observed) in regions.items():
        reference_image, actual_image = raster(expected, font), raster(observed, font)
        difference = ImageChops.difference(reference_image, actual_image)
        pixels = sum(pixel != (0, 0, 0) for pixel in difference.getdata())
        characters = sum(a[0] != b[0] for row_a, row_b in zip(expected, observed) for a, b in zip(row_a, row_b))
        reference_image.save(args.output / f'{name}_reference.png')
        actual_image.save(args.output / f'{name}_csharp.png')
        difference.save(args.output / f'{name}_diff.png')
        report['regions'][name] = {'differentCharacters': characters, 'differentPixels': pixels}
    (args.output / 'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))
    return int(any(value['differentPixels'] for value in report['regions'].values()))


if __name__ == '__main__':
    raise SystemExit(main())
