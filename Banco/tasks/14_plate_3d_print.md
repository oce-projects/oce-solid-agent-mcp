# 14 — 3D-print the plate

**Family:** exports

## The task

> Send the slotted plate (task 03) off for 3D printing: fine quality is fine.

## Why this task

The STL export, in this bridge, checks the part on its own before writing the file and refuses if it isn't a closed solid. This task is the **positive** case: a healthy, single-body part that must go through without a hitch. It is the mirror image of the case already in the bench where two separate bodies are rightly refused: here a refusal would be the bug, not the safeguard.

## What it deliberately doesn't say

It doesn't give a file path: by default the file is written next to the part, and asking for it explicitly would only measure the ability to copy a path the task itself would have already given.

## How it is measured

| what | expected value | tolerance | why |
|---|---|---|---|
| bodies | 1 | — | precondition: a single body, as in task 03. A multi-body part shouldn't even reach the export step |
| measurements.exported_anyway (of the `export_stl` step) | absent | — | the soundness check the operation runs on its own before writing the file must come back positive: the answer carries `exported_anyway` only when a part the check found unsound was written on request |
| measurements.byte (of the `export_stl` step) | > 0 | — | the file must be written, not refused, and its size is read back from the disk: unlike the "two bodies don't export" case already in the bench, here a refusal would be the bug |
| measurements.quality | "fine" | — | the quality explicitly asked for ("fine is fine") must be the one used, not the coarse setting that makes a smaller file but facets round features — and this plate has a slot with two round ends |

## What this measurement does not see

Whether the written file is actually printable on a real printer (walls too thin, a chamfer creating an overhang the printer can't hold): SOLIDWORKS's check verifies the solid is closed and sound, not that it's printable on a specific machine. Those are two different checks, and only the first is measured here.
