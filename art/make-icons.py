"""Turn the logo into the app's icons.

    python art/make-icons.py

Reads art/millburn-logo.png and writes src/MillBurn.App/Assets/millburn.png and .ico.

Only the mark is used, not the wordmark: at 16 px a taskbar icon is about eleven pixels of usable
area, and "PCB_MillBurn" set across it is a grey smear. The mark survives because it is one shape
with a strong silhouette.

The background is removed by flooding in from the edges rather than by making white transparent,
because the design's own outlines are white too and keying on colour would eat them.
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)

SOURCE = os.path.join(HERE, "millburn-logo.png")
OUT = os.path.join(ROOT, "src", "MillBurn.App", "Assets")

# Where the wordmark starts. The logo has a clear band of background between the two, and this sits
# in it; see the gap search in the commit that added this.
WORDMARK_TOP = 508

# Windows shows the .ico at whichever of these fits; below 32 the mark is all that reads at all.
ICO_SIZES = [16, 24, 32, 48, 64, 128, 256]

# Room around the mark so it does not touch the edge of its tile. Kept small: the mark is half as
# wide again as it is tall, so squaring it already costs a third of the height, and at 16 px every
# pixel spent on margin is one the artwork does not get.
MARGIN = 0.02

# Below this, a plain downscale turns the traces to grey mush -- they are thin white lines with
# thin black outlines, which is the worst possible thing to resample. A little unsharp masking
# pulls the edges back apart. It does nothing useful at larger sizes, so it is not applied there.
SHARPEN_BELOW = 48


def sharpened(frame):
    """Unsharp the colour only, leaving alpha alone.

    Sharpening all four channels together overshoots on the alpha edge as well as the colour one,
    and the two overshoots do not line up: the result is a rim of orange fringing wherever white
    trace meets blue board. Splitting them off keeps the edge crisp and the silhouette clean.
    """
    r, g, b, a = frame.split()
    rgb = Image.merge("RGB", (r, g, b))
    rgb = rgb.filter(ImageFilter.UnsharpMask(radius=1.0, percent=110, threshold=2))

    r, g, b = rgb.split()
    return Image.merge("RGBA", (r, g, b, a))


def main():
    if not os.path.exists(SOURCE):
        sys.exit("missing " + SOURCE)

    im = Image.open(SOURCE).convert("RGBA")
    mark = im.crop((0, 0, im.width, WORDMARK_TOP))

    # Flood the background away from all four corners. A tolerance of 60 covers the slight
    # gradient in the generated art without reaching through the black outline.
    filled = mark.copy()
    for corner in [(0, 0), (mark.width - 1, 0), (0, mark.height - 1), (mark.width - 1, mark.height - 1)]:
        ImageDraw.floodfill(filled, corner, (0, 0, 0, 0), thresh=60)

    # Anything the flood reached is now fully transparent; everything else keeps its pixel.
    px = filled.load()
    for y in range(filled.height):
        for x in range(filled.width):
            r, g, b, a = px[x, y]
            if (r, g, b, a) == (0, 0, 0, 0):
                px[x, y] = (0, 0, 0, 0)

    box = filled.getbbox()
    if box is None:
        sys.exit("nothing left after removing the background")

    cropped = filled.crop(box)

    # Square, centred, with a margin. A non-square icon is stretched by some shells and letterboxed
    # by others, and neither looks deliberate.
    side = int(max(cropped.width, cropped.height) * (1 + 2 * MARGIN))
    square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    square.paste(
        cropped,
        ((side - cropped.width) // 2, (side - cropped.height) // 2),
        cropped)

    os.makedirs(OUT, exist_ok=True)

    png = square.resize((512, 512), Image.LANCZOS)
    png.save(os.path.join(OUT, "millburn.png"), optimize=True)

    # Each size resampled from the full-resolution square rather than from the one above it, so the
    # small ones stay as sharp as they can be.
    frames = []
    for s in ICO_SIZES:
        frame = square.resize((s, s), Image.LANCZOS)

        if s < SHARPEN_BELOW:
            frame = sharpened(frame)

        frames.append(frame)

    frames[-1].save(
        os.path.join(OUT, "millburn.ico"),
        format="ICO",
        sizes=[(s, s) for s in ICO_SIZES],
        append_images=frames[:-1])

    print("mark %dx%d -> %dx%d square" % (cropped.width, cropped.height, side, side))
    print("wrote millburn.png (512) and millburn.ico (%s)" % ", ".join(str(s) for s in ICO_SIZES))


if __name__ == "__main__":
    main()
