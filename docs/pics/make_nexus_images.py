"""Builds the Nexus / README images in docs/pics/nexus:

* cover.jpg (1920x1080): main image, the map without and with the mod, split on a slant, the title on top
* animals.jpg: the smith asking for a Bear Skull and the bears on the map
* panel.jpg: the Markers panel in the map's Filters menu
* challenge.jpg: a "Kill N" challenge and the map with that faction's markers
* caves.jpg, valuables.jpg, navigation.jpg: the map with those markers, captioned
* minimap.jpg: the markers on the Minimap mod's minimap (its raw shots, the layout of its map-markers.jpg)
* settings.jpg: the Options rows
* header.jpg (1300x372): Nexus page header / README top

Raw screenshots (git-ignored, docs/pics/*.png): taken on a 9:8 monitor with the game at 16:9, so crop16x9 finds the
game area. Needs Pillow and numpy. Usage: python make_nexus_images.py
"""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "nexus")
FONT = "C:/Windows/Fonts/segoeuib.ttf"
FONT_LIGHT = "C:/Windows/Fonts/segoeui.ttf"
WHITE, GOLD, GREY = (255, 255, 255), (216, 201, 163), (187, 187, 187)
W, H = 1920, 1080
SIZE, MARGIN = 300, 20  # Minimap mod's Size and EdgeMargin (HUD units) when 07_minimap was taken


def font(size, bold=True):
    return ImageFont.truetype(FONT if bold else FONT_LIGHT, size)


def shot(name):
    return crop16x9(Image.open(os.path.join(HERE, name + ".png")).convert("RGB"))


def crop16x9(im):
    """The 16:9 game area: full width, from the first non-black row."""
    w, h = im.size
    ch = round(w * 9 / 16)
    gray = np.asarray(im.convert("L"))
    rows = np.where(gray.max(axis=1) > 24)[0]
    top = int(rows[0]) if len(rows) else 0
    top = min(top, h - ch)
    return im.crop((0, top, w, top + ch))


def full(name):
    return shot(name).resize((W, H), Image.LANCZOS)


def window(im, box):
    """A part of a full-HD frame, scaled back up to full HD (box in 1920x1080 coordinates, 16:9)."""
    return im.crop(box).resize((W, H), Image.LANCZOS)


def blur_footer(img, top=1000):
    """The game's footer prompts (garbled in some shots: two prompt sets mid-transition)."""
    band = img.crop((0, top, W, H)).filter(ImageFilter.GaussianBlur(18))
    img.paste(ImageEnhance.Brightness(band).enhance(0.6), (0, top))
    return img


def top_shade(img, height=300, strength=0.9):
    """Darken the top for a caption, fading out downwards."""
    g = np.clip(1 - np.arange(H) / height, 0, 1) ** 1.2 * 255 * strength
    shade = Image.fromarray(np.tile(g.astype(np.uint8)[:, None], (1, W)))
    return Image.composite(Image.new("RGB", img.size, (6, 5, 4)), img, shade)


def caption(img, title, sub=None, x=80, y=50):
    img = top_shade(img)
    d = ImageDraw.Draw(img)
    d.text((x, y), title, font=font(64), fill=WHITE)
    if sub:
        d.text((x + 3, y + 88), sub, font=font(34, False), fill=GOLD)
    return img


def shadow(img, box, radius=40, opacity=200, ellipse=True):
    sh = Image.new("L", img.size, 0)
    (ImageDraw.Draw(sh).ellipse if ellipse else ImageDraw.Draw(sh).rectangle)(box, fill=opacity)
    sh = sh.filter(ImageFilter.GaussianBlur(radius))
    return Image.composite(Image.new("RGB", img.size, (0, 0, 0)), img, sh)


def round_mask(size, inset=0.0):
    m = Image.new("L", (size * 4, size * 4), 0)
    i = size * 4 * inset
    ImageDraw.Draw(m).ellipse((i, i, size * 4 - i, size * 4 - i), fill=255)
    return m.resize((size, size), Image.LANCZOS)


def rounded(img, radius=18):
    m = Image.new("L", (img.width * 2, img.height * 2), 0)
    ImageDraw.Draw(m).rounded_rectangle((0, 0, m.width - 1, m.height - 1), radius * 2, fill=255)
    return m.resize(img.size, Image.LANCZOS)


def dim(img, factor):
    return ImageEnhance.Brightness(img).enhance(factor)


def save(img, name):
    img.convert("RGB").save(os.path.join(OUT, name), quality=92)


# ------------------------------------------------------------------ images

# The map shots without the game's header and footer: the middle of the frame, 16:9.
MAP_BOX = (205, 135, 1695, 973)


def cover():
    """Main image: the same map view without (left) and with the mod (right), split on a slant."""
    vanilla = window(full("11_vanilla_map"), MAP_BOX)
    mod = window(full("01_map_overview"), MAP_BOX)
    vanilla = dim(ImageEnhance.Color(vanilla).enhance(0.6), 0.75)
    mask = Image.new("L", (W, H), 0)
    ImageDraw.Draw(mask).polygon([(760, 0), (W, 0), (W, H), (560, H)], fill=255)
    img = Image.composite(mod, vanilla, mask.filter(ImageFilter.GaussianBlur(3)))
    d = ImageDraw.Draw(img)
    d.line([(760, 0), (560, H)], fill=GOLD, width=5)
    img = top_shade(img, 330, 0.93)
    d = ImageDraw.Draw(img)
    d.text((W // 2, 40), "More Map Filters", font=font(140), fill=WHITE, anchor="mt")
    d.text((W // 2, 205), "Animals, enemies, resources, loot and shortcuts on your map", font=font(48, False), fill=GOLD, anchor="mt")
    for text, x in (("Vanilla", 230), ("With the mod", 1500)):
        tw = d.textlength(text, font=font(44))
        d.rounded_rectangle((x - tw / 2 - 24, 985, x + tw / 2 + 24, 1055), 16, fill=(10, 8, 6))
        d.text((x, 1020), text, font=font(44), fill=WHITE, anchor="mm")
    save(img, "cover.jpg")


def panel():
    img = window(blur_footer(full("02_panel")), (190, 130, 1730, 996))
    save(caption(img, "Pick what you are looking for", "A Markers panel in the map's Filters menu: one button per type"), "panel.jpg")


def animals():
    """The smith asking for a Bear Skull (left), the bears on the map (right)."""
    smith = full("12_upgrade_moonshaft")
    bears = full("13_animals_map")
    img = dim(bears.filter(ImageFilter.GaussianBlur(16)), 0.3)
    img = caption(img, "Need a Bear Skull? Here are the bears",
                  "Wolves, stags, sirens, boars, winged broods and bears: the parts your upgrades ask for")
    card_box = (590, 112, 1055, 1020)                 # the item card, down to the Upgrade button
    k = 780 / (card_box[3] - card_box[1])
    card = smith.crop(card_box)
    card = card.resize((int(card.width * k), int(card.height * k)), Image.LANCZOS)
    cx, cy = 90, 255
    img = shadow(img, (cx - 10, cy - 10, cx + card.width + 10, cy + card.height + 10), 30, 220, False)
    img.paste(card, (cx, cy), rounded(card, 14))
    # the Bear Skull row (y ~ 841 in the frame)
    d = ImageDraw.Draw(img)
    ry = cy + int((841 - card_box[1]) * k)
    d.rounded_rectangle((cx + 4, ry - 21, cx + card.width - 4, ry + 21), 10, outline=GOLD, width=4)
    map_box = (470, 170, 1530, 766)                   # the bears around the hero, Marin Woods
    tile = bears.crop(map_box).resize((1290, 725), Image.LANCZOS)
    mx, my = 570, 285
    img = shadow(img, (mx - 10, my - 10, mx + tile.width + 10, my + tile.height + 10), 30, 220, False)
    img.paste(tile, (mx, my), rounded(tile, 14))
    d = ImageDraw.Draw(img)
    for x, y in ((765, 537), (822, 582), (845, 680), (1690, 715)):   # the bear markers on the tile
        d.ellipse((x - 36, y - 36, x + 36, y + 36), outline=GOLD, width=3)
    save(img, "animals.jpg")


def challenge():
    """The challenge on the left, the faction's markers on the right."""
    risen = window(full("03b_risen_map"), (0, 40, 1500, 884))
    img = dim(risen.filter(ImageFilter.GaussianBlur(16)), 0.3)
    img = caption(img, "\"Kill 10 Risen\"? Here they are", "Every faction the daily and weekly challenges ask for")
    ch = full("03a_challenge_board")
    left = ch.crop((470, 190, 1450, 640)).resize((780, 358), Image.LANCZOS)
    right = risen.resize((1000, 563), Image.LANCZOS)
    img = shadow(img, (40, 400, 860, 760), 40, 220, False)
    img = shadow(img, (860, 280, 1900, 880), 40, 220, False)
    img.paste(left, (60, 400), rounded(left))
    img.paste(right, (880, 300), rounded(right))
    d = ImageDraw.Draw(img)
    d.text((60 + 390, 790), "The challenge", font=font(34), fill=GREY, anchor="mt")
    d.text((880 + 500, 900), "Map: The Risen", font=font(34), fill=GREY, anchor="mt")
    save(img, "challenge.jpg")


def map_shot(src, name, title, sub, box=MAP_BOX):
    img = window(full(src), box)
    save(caption(img, title, sub), name)


MINIMAP_PICS = os.path.join(HERE, "..", "..", "..", "Minimap", "docs", "pics")


def minimap():
    """Same layout as the Minimap mod's map-markers.jpg (its raw shots of one spot): the map left, the minimap right."""
    def mm_shot(name):
        return crop16x9(Image.open(os.path.join(MINIMAP_PICS, name + ".png")).convert("RGB"))
    big = mm_shot("05b_map").resize((W, H), Image.LANCZOS)
    mini = mm_shot("05_map_markers")
    img = Image.new("RGB", (W, H), (10, 9, 8))
    win = big.crop((W // 2 - 620, H // 2 - 400, W // 2 + 380, H // 2 + 360)).resize((1000, 760), Image.LANCZOS)
    img.paste(win, (60, 220))
    size = 720
    img = shadow(img, (1130, 230, 1130 + size + 60, 230 + size + 60), 40, 230)
    w, h = mini.size
    side, m = SIZE * h / 1080, MARGIN * h / 1080
    x1, y1, p = w - m, h - m, side * 0.06
    mm = mini.crop((int(x1 - side - p), int(y1 - side - p), int(x1 + p), int(y1 + p))).resize((size, size), Image.LANCZOS)
    img.paste(mm, (1160, 260), round_mask(size, 0.06 / 1.12 + 0.004))
    d = ImageDraw.Draw(img)
    d.text((60, 52), "Goes well with the Minimap mod", font=font(64), fill=WHITE)
    d.text((63, 140), "A separate mod that puts the map on your HUD: the markers show on it too",
           font=font(30, False), fill=GOLD)
    d.text((560, 1050), "Map", font=font(34), fill=GREY, anchor="ms")
    d.text((1160 + size // 2, 1050), "Minimap", font=font(34), fill=GREY, anchor="ms")
    save(img, "minimap.jpg")


def settings():
    """The Options rows (below the game's tab bar) with Enemies selected, its description showing the group's markers."""
    src = full("08_settings_category")
    img = dim(src.filter(ImageFilter.GaussianBlur(16)), 0.3)
    img = caption(img, "Settings", "Choose the groups, the marker size and markers in unexplored areas")
    box = (90, 210, 1730, 1040)                       # rows and description, without tabs and footer
    k = 0.97
    part = src.crop(box)
    part = part.resize((int(part.width * k), int(part.height * k)), Image.LANCZOS)
    at = ((W - part.width) // 2, 240)
    img = shadow(img, (at[0] - 10, at[1] - 10, at[0] + part.width + 10, at[1] + part.height + 10), 30, 220, False)
    img.paste(part, at, rounded(part, 16))
    # a label at the preview: the group's markers under the description
    d = ImageDraw.Draw(img)
    px, py = at[0] + int((1080 - box[0]) * k), at[1] + int((480 - box[1]) * k)
    d.line((px + 60, py + 20, px + 60, py + 90), fill=GOLD, width=3)
    d.text((px + 20, py + 100), "Live preview of the setting", font=font(34), fill=GOLD)
    save(img, "settings.jpg")


def header():
    """1300x372: a window of the with-mod map on the right, fading to black under the title on the left."""
    mod = window(full("01_map_overview"), MAP_BOX)
    img = mod.crop((520, 380, 520 + 1300, 380 + 372))
    x = np.arange(1300)
    g = np.clip((1 - x / 760.0) * 1.6, 0, 0.94) * 255   # dark under the title on the left, the map on the right
    shade = Image.fromarray(np.tile(g.astype(np.uint8), (372, 1)))
    img = Image.composite(Image.new("RGB", img.size, (0, 0, 0)), img, shade)
    d = ImageDraw.Draw(img)
    d.text((48, 150), "More Map Filters", font=font(76), fill=WHITE, anchor="lm")
    d.text((51, 232), "Animals, enemies, resources, loot and shortcuts on your map", font=font(28, False), fill=GOLD, anchor="lm")
    save(img, "header.jpg")


def main():
    os.makedirs(OUT, exist_ok=True)
    cover()
    animals()
    panel()
    challenge()
    map_shot("04_caves", "caves.jpg", "Caves show at their entrance",
             "What is inside, with a doorway badge; \"?\" when it is only maybe")
    map_shot("05_valuables", "valuables.jpg", "Valuables",
             "Treasure chests, special shinies and keys (off by default)")
    map_shot("06_navigation", "navigation.jpg", "Navigation",
             "Whispers you have not found yet and shortcuts you have not opened")
    minimap()
    settings()
    header()
    print("wrote", sorted(os.listdir(OUT)))


if __name__ == "__main__":
    main()
