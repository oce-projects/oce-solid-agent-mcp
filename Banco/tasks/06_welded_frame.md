# 06 — One-metre welded frame

**Family:** weldments

## The task

> Make me a welded frame out of square tube, one metre by eighty, mitre-cut at the corners. Then put the cut list on it.

## Why this task

It tests the thing a model usually cannot do: **picking a profile from this shop's library**. The task doesn't say which tube — it says "square tube" — and the profile's size lives in a `.SLDLFP` file on a disk the model has never seen. To get there it must ask for the weldment profile list, read it, and copy the size **verbatim**: "80X80X5" with a capital X, because SOLIDWORKS is literal there.

It is also the only task in the folder that asks for two things in sequence, where the second ("put the cut list on it") only makes sense if the first went well.

## What it deliberately doesn't say

It doesn't say the tube's section, and it doesn't say the library path. A task that gave them would measure the ability to copy two strings. Getting this wrong is the interesting case, not an accident: the refusal the model gets names the real sizes, and whether it gets there on a second try is a different number, counted separately.

## How it is measured

| what | expected value | tolerance | why |
|---|---|---|---|
| bodies | 4 | — | four bars. Three means a missing side, eight means the frame was built twice |
| measurements.bodies_in_cut_list | 4 | — | and this number comes from somewhere else: "bodies" counts the part's state, "bodies_in_cut_list" is read from the cut-list rows, one by one. They match when the cut list is correct, and when they diverge is exactly the moment someone would walk to the shop floor with the wrong sheet |
| measurements.already_existed | false | — | the cut list must have been born in this plan. Without this line the task would pass green even if the operation had done nothing and simply read a cut list that already existed |

## What this measurement does not see

Whether the mitre cut is really there. Four bars and four cut-list rows are also obtained with square-cut corners, and the frame still gets welded — badly. Seeing that needs reading the corner treatment or the bar lengths: 1000 and 800 with a mitre, different with a square cut. It also doesn't see **which** profile was chosen: a 40x40x3 tube passes the same checks. The task says "square tube" and not which one, so that is consistent — but whoever reads the result must not believe it was verified.
