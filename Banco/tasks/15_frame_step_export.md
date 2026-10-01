# 15 — STEP export of the stand for the welder

**Family:** exports

## The task

> Export the tube stand with feet (task 07) as STEP, so the welder can cut it on their CNC. Also run the check that the file reads back as the same part.

## Why this task

Unlike the 3D print (task 14), this part has **seventeen bodies**: the STEP file must carry all of them, not just the first one found. The task explicitly asks for the reopen-and-compare check too, which the catalogue offers as an option for exactly this reason: a STEP file that drops a body along the way still opens fine, and without this check nobody would notice until the welder counts the cut pieces and one is missing.

## What it deliberately doesn't say

It doesn't say which STEP protocol (AP214, AP203, AP242): the catalogue has a default (AP214) that also carries colours, well suited to this use. Asking for it explicitly would only measure the ability to copy a name, not the choice.

## How it is measured

| what | expected value | tolerance | why |
|---|---|---|---|
| measurements.export_succeeded | true | — | the file must be written |
| measurements.bodies_in_file | 17 | — | the same body count as the part (task 07): a lower number means the export lost or merged bodies along the way, a defect that may not jump out when the file is opened in a generic viewer |
| measurements.volume_mismatch | < 0.001 | — | the reopen-and-compare check must find the volume difference between the part and the freshly written file within one-thousandth: beyond that threshold the operation itself flags it as failed |

## What this measurement does not see

Whether the welder can actually open this STEP in their CNC software: AP214 is the most widely supported, but not the oldest (that's AP203) nor the only one. The reopen check proves SOLIDWORKS itself reads the file back without losing anything, not that a third-party program reads it the same way.
