# 13 — Lid interference

**Family:** checks

## The task

> Before sending the box assembly (task 08) to production, check that the lid doesn't interfere with the base: it should only rest on it, not wedge in.

## Why this task

The catalogue deliberately distinguishes a resting lid (not an error) from two faces that truly overlap (an error): the interference check, by default, doesn't count two exactly-touching faces as interference. This task checks that the model leaves that default behaviour in place — which is the right one for this assembly — instead of tightening it out of over-caution and finding an interference where there is none.

## What it deliberately doesn't say

It doesn't say how the check should be configured (whether to count exactly-touching faces or not): the task describes the wanted result ("should only rest on it"), and picking the right configuration to get there is the model's job.

## How it is measured

| what | expected value | tolerance | why |
|---|---|---|---|
| measurements.interferences | 0 | — | the lid in task 08's assembly rests on the base without overlapping it: zero is the correct result, not a defect to fix |

## What this measurement does not see

Whether the check was run with the stricter setting that also counts exactly-touching faces as interference (a setting useful for "does this shaft fit this hole?" but not for a resting lid): with that setting the numeric result could change for this very assembly, and a non-zero count wouldn't be a defect either way — it would be the wrong question put to the right check. Knowing which needs reading which setting was used, not the number alone.
