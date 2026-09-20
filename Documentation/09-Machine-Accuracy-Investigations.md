# 09 — Machine Accuracy Investigations

Root-cause analyses of alignment errors found on real machines, kept because **the reasoning is
worth more than the conclusion**. Each one records what was measured, what was concluded, and where
the conclusion turned out to be wrong — a wrong turn that is written down cannot be taken twice.

These are not help pages. `Help/` tells an operator what to do; this tells whoever maintains the app
what the machines actually did, and why some features exist.

---

## 1. The waste-hole crosses that did not line up — 2026-09-19

### What was being done

The V0.1.5 workflow test: 1.6 mm double-sided clad, stock cut on the mill to **78.63 × 76.09 mm**
with the 0.8 mm end mill, its two alignment holes drilled in the waste at **X76.830 Y2.500** and
**X2.500 Y74.290** — 74.33 mm apart across, 71.79 up, **103.338 mm** on the diagonal. The stock was
then painted matt black and its top copper burned on a Creality Falcon, with 6.17's placing layers
in the file: the board outline in red, the stock and a ring-and-cross at each hole in blue.

Photographed afterwards, the cross at the top-left hole sat almost on it; the one at the bottom-right
was clearly outside it. One corner right and the opposite corner out is the signature of a scale
error, and that is the first thing everyone reached for. It was not a scale error.

### The measurements, in the order they arrived

| # | Measurement | Reading | What it ruled out |
|---|---|---|---|
| 1 | Cross-to-hole offsets, read off photographs | 0.14 mm at one corner, 0.7 mm at the other | nothing — see the parallax note below |
| 2 | A 100 mm line burned on the laser, each axis | 100 mm ± 0.05 | the laser's **scale** |
| 3 | Falcon's reported size of the imported SVG | 78.63 × 76.09 mm | the **import** |
| 4 | Falcon's own ruler, cross to cross | 103.34 mm | the **file** |
| 5 | The cut stock, with calipers | 78.6 × 76.16 against 78.63 × 76.09 | the mill's **scale** |
| 6 | 0.8 mm pins in both holes: over the outsides, between the insides | 103.9 and 102.3 → **centre 103.10 mm** | — the holes are **0.24 mm close** |
| 7 | The same design burned on MDF, cross to cross | **103.6 mm** | — the burn is **0.26 mm long** |
| 8 | An X burned on MDF, 140 mm legs, both diagonals | **140.4** and **139.8 mm** | — the laser is **out of square** |

### The conclusion

**Two independent errors, leaning opposite ways, on the same diagonal.**

- **The laser is out of square by about 0.25°.** Measurement 8: a 0.6 mm difference between the two
  diagonals of an X with 140 mm legs is `asin(0.6 / 140)`. Their mean, 140.1 mm, says the axis
  lengths themselves are right — which is why measurement 2 passed. A skew shears the drawing
  instead of stretching it: nothing moves along X or Y, but anything on a diagonal does, by an amount
  that grows with distance from wherever the operator placed the design. Over this board's 103 mm
  diagonal, 0.26 mm — which is measurement 7, arrived at by a different route. Predicted from
  measurement 7 alone, the skew is 0.29°; measured directly, 0.246°.
- **The mill's two holes are 0.24 mm closer than the program asks** (measurement 6), while the piece
  they are in is the right size (measurement 5). A piece that measures true with holes in it that do
  not is a *positioning* error, not a scale one. Two candidates remain, and they are told apart by
  the four-hole check in 6.22: **backlash** of about 0.17 mm on X, taken up from opposite sides
  because the two holes are approached from opposite directions; or the mill being **out of square**
  too, by about 0.13°, leaning the other way from the laser.

The two errors partly cancel — the mill's diagonal short, the laser's long — so the finished board
came out better than either machine on its own would suggest. That is luck, not design, and it is the
kind of luck that reverses when a machine is repaired.

### The wrong turns, and what they cost

1. **"The laser's X scale is 0.8% out."** From the photographs alone, and stated with far too much
   confidence. A single burned 100 mm line killed it in about a minute. *Lesson: a hypothesis that
   can be tested for the price of one line should be tested before it is explained.*
2. **"Backlash of 0.27 mm."** Built on the photographs and on a first caliper reading of 102.8 mm
   taken across two 0.8 mm holes. Pins in the holes gave 103.10 mm, and a self-check came with it:
   outside minus inside was 1.60 mm, exactly two pin diameters. The real figure is less than half the
   claim, and a design built around the wrong number would have overshot every hole by three times
   what it needed.
3. **"The photographs are worthless — it is all parallax."** An over-correction in the other
   direction. Parallax *is* real here: the hole goes through 1.6 mm of board, so a camera 15–20° off
   vertical shifts the dark circle's apparent centre by up to half a millimetre relative to a cross
   burned on the surface, and it is why the top and bottom runs disagreed about Y. But measurements 6
   and 7 together — holes 0.24 short, burn 0.26 long, 0.5 mm apart — match what the photographs
   showed along X. They were right about the size of the mismatch and wrong only about whose fault it
   was.

### What to measure, and how

The techniques that worked, all with digital calipers and a loupe:

- **Pins, not hole edges.** Put identical pins in two holes, read **over** the outsides and
  **between** the insides, and average: that is the centre distance, with the pin diameter cancelling.
  It is the only reliable way to get a centre-to-centre reading out of calipers, and
  `outside − inside = 2 × pin` is a free check that the reading is sound.
- **Diagonals for squareness.** No single-axis measurement can see a skew; two diagonals of a square
  see nothing else. A difference `d` across a diagonal `D` is a skew of `asin(d / D)`.
- **One long baseline beats ten short ones.** Every error here is a fraction of a percent, and a
  caliper reads 0.01 mm: measure over 100 mm and the error is ten times its own uncertainty.
- **Cross-check by an independent route.** The skew predicted from the burned design (0.29°) and the
  skew measured from an X (0.246°) agree, and that agreement is what makes the conclusion safe.
- **Beware a through-hole in a photograph.** Shoot square over the feature, or measure it.

### What it changed

- **6.21** — every hole approached from the same side, so backlash is taken up identically. Written
  before the numbers were firm; its figures were corrected to the ones above.
- **6.22** — machine checks, with the backlash check specified in full and the squareness check
  sketched. This investigation is the argument for that section: **every** single-axis check the app
  or the operator can run passed, and the machine was still wrong.
- **6.20** — which way up is this stock. The chamfer that marks the datum corner also removed the one
  corner needed to measure the stock's own diagonals, so the mill's squareness could not be checked
  on the piece that was already on the bench.
- A hand-written `Squareness_Check_60mm.nc` (four holes at the corners of a 60 mm square, every one
  approached from below-left so backlash cancels and only squareness is left) went to the workshop.
  Both diagonals should read 84.853 mm; about 0.20 mm of difference would confirm the mill is out of
  square rather than loose.

### Still open

The mill's four-hole check, and then squaring the laser's gantry — the usual loop of burning a large
square, shifting one end of the gantry along Y to shorten the long diagonal, and burning it again.
Neither Falcon's software nor GRBL can compensate for a skew, so the frame is the only place to fix
it.
