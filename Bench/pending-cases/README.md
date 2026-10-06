# Cases written before the thing they measure

**The bench does not look at this folder**: it scans `Bench/cases/` and nothing
else (`DeclaredCases.cs:94`). In here live the cases that exercise a behaviour
**that does not exist yet**, written in advance.

## Why it exists, and it is not a drawer

A case that must fail, put in `cases/`, turns the bench red for a known reason
— and a bench that is red for a known reason is **a bench that stops saying
anything**: whoever looks at it learns to ignore that line, and the day it turns
red for another reason nobody notices.

It is the same shape as `Bench/tasks/` (entry Z-G9): *the input is born before
whoever runs it, with its measurement next to it.*

## The rule for leaving here

A case moves to `Bench/cases/` **in the same commit** in which the behaviour it
measures arrives, and that commit carries a green bench run. Not before: a case
in `cases/` is a promise that the run keeps.

## What is here now

**One case, from 28/9/2026 (Z-C21.10, C8-2):**
`a_dimension_hidden_by_hand_does_not_count.json`. It measures that a drawing
dimension hidden by hand (IAnnotation.Visible = swAnnotationHidden) does not
count as «already dimensioned» in `drawing_dimension`. The library does it; the
bench cannot build the situation yet: no catalogue operation hides a dimension,
and the runner has no preamble field for it. It moves to `Bench/cases/drawing/`
in the commit that brings the runner field `hide_dimensions_first`
(`{"view": "V1"}`: every display dimension of that view is selected with
IAnnotation.Select3 and hidden with IModelDoc2.HideDimension, then read back;
one that is not swAnnotationHidden is a bench error), with a bench run.

### Before, from 24/9/2026

**Nothing, and that is the right thing.** The four cases of Z-G3 were moved to
`Bench/cases/` on 24/9/2026, the same day on which the two `default` behaviours
arrived — `document_properties/default_material.json` with its
`requested_material.json`, `bodies/default_combine.json` with its
`requested_combine.json`.

The folder stays empty with this file in it, because the shape is still needed:
the next time a case is born before the thing it measures, it goes here.

### Something that happened while they were here, and is worth remembering

The two cases of `combine_bodies` put the bodies **120 mm** apart, to make the
three outcomes unmistakable (union = the sum, subtraction = the main body alone,
intersection = zero). Rereading the description of the operation before moving
them, it turned out that **`combine_bodies` declares that it fails** when after
the union the bodies are still two: *«usually they do not really touch, and I
fail instead of confirming a part different from the one you think you have»*.

Two bodies far apart do not fuse. The union would have failed, and **the red
case would have accused the default instead of the geometry** — that is, it
would have sent people looking for the defect in the wrong place. Now they
overlap by 40 mm and the three numbers stay distinct: 80000, 30000, 20000.

*A case is reread against the description of the operation it exercises, not
only against the schema: the schema says what it accepts, the description says
what it refuses.*

## The contract decision that these two cases fix

A default **is not one field less**: it is a choice made by somebody else.
Entry Z-G3 was born exactly from there — *«nobody thought about it» and «somebody
thought about it and decided» read the same in the JSON, and only one of the two
can be improved.*

It holds identically the other way round: if the bridge puts S235JR and does not
say so, the model **believes it chose it itself**, and next time it will not even
ask. Then a part arrives that should have been in stainless steel and nobody
knows where the decision was taken.

So the cases are not content with the operation succeeding. They demand that the
answer **state the assumption**: which value, and that it was assumed, not asked
for. They demand **two things** to be there — the value (`S235JR`, `union`) and
the phrase «you have not asked for it». The value alone would say only what is
there now; the phrase says **who chose it**, and it is the only difference
between a help and a decision taken behind your back.

## Something that these cases propose and have no right to decide

The field they look at is called **`measurements.note_assumption`** — it was
called `measurements.assumed` while it was a proposal, and it was aligned with the
`note_*` convention that the repository already used (`note_identifiers`,
`note_volume`) when it was implemented, on 24/9/2026. Whoever implements it can
call it differently or put it elsewhere — in that case the `what` line of the
four cases is changed, which takes a minute.

What is **not** changed is the demand: the answer must tell «I chose it» from
«the bridge chose it for me». The name of the field is a detail; that distinction
is the entry.
