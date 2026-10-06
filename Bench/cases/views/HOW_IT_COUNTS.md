# V2 — how it is counted

*Written on 15/9/2026 **before** looking at the answers. A criterion chosen after
seeing the results is not a criterion: it is a conclusion in disguise.*

## The four conditions

The same material, the same question, four **isolated** models (nobody sees the
answers of the others, nobody sees `-truth.txt`, the file with the true values):

| | what it receives |
|---|---|
| **A** | only the **isometric** view |
| **B** | **four views**: isometric, front, top, right |
| **C** | only the **textual state**, that is, what the bridge gives today |
| **D** | state **plus** isometric |

A and B together answer the question of the entry — «is an isometric enough, or
are the three standard ones needed?». C is the term of comparison: the bridge
before 0.31.0. D is what the bridge does now.

## The four columns

Three points per part, fifteen per condition, plus a count that is not a score.

- **FAMILY** (0 or 1) — it said what object it is. Right or wrong, no half
  points: «drilled plate» is right, «plate» is not, because the missing half is
  exactly the one being measured.
- **FEATURES** (0 or 1) — how many **and** of what type. The number alone is not
  enough and neither is the type alone.
- **DIMENSIONS** (0 or 1) — the bounding box within **±10%** plus at least one
  characteristic dimension (thickness, diameter, hole spacing) within ±10%.
- **INVENTIONS** (a count) — how many numbers it declared that are wrong by more
  than 10%. **It is not subtracted from the score: it is looked at apart**, and
  it is the most important column of all.

### Why the inventions are counted apart

An image carries no scale: whoever looks at a render alone **cannot** know
whether the plate is 200 or 400 mm long. The honest answer there is «not
estimable», and it is worth zero points in DIMENSIONS.

Zero points for honesty and zero points for a wrong number add up the same, and
they are not the same thing. A «I don't know» costs the reader one more
question; an invented number goes into the part. **It is the criterion of this
whole project applied to its own measurement:** a refusal to answer is not a
defect, a wrong answer that presents itself as good is.

So the verdict reads like this: **DIMENSIONS says how much the state is needed,
FAMILY and FEATURES say how much the image is needed, INVENTIONS says whether
either of the two invites guessing.**

## What it does NOT measure

- It does not measure cost. That is known: a 1024x768 PNG is ~70-115 kB, that is,
  more than ten times the whole written state (3,600-8,000 characters over the
  five shapes). The ceiling of three views remains a cost choice.
- It does not measure real parts. Five shapes chosen by whoever writes the
  measurement are not a sample: they are five cases chosen to be **different from
  one another** (plate, thin box, bent bracket, flange with a crown of holes,
  revolution). If the result were close, real parts would be needed.
- It does not measure the same thing as V3. There the question is whether the
  shape can be **said**; here the question is whether it must be **seen**.

---

## What was discovered by measuring, and it holds for all three entries

Three measurements — V2 (how many views), V3 (the shape told in words), V4 (the
cutaway) — said the same thing from three different sides:

> **An image carries a SHAPE. A number lives in the text.**

- **V2** — with the images alone: family 5/5, dimensions 0/5, and **zero invented
  numbers** («not estimable, no scale reference»). With the state alone:
  everything right, but **three wrong derived numbers** with full confidence.
- **V3** — the sentence, which already carries the numbers worked out: **five
  parts out of five** are redrawn at the exact volume.
- **V4** — the section **reveals** a thickness that did not exist from the
  outside, but in isometric it is read wrong by 40-50%: **only an orthogonal view
  carries a number**.

The exception of V4 is the only one, and it is the one worth having.
