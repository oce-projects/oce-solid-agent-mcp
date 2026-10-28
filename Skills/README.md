# Skills for the AI that drives the bridge

The bridge gives the AI the operations. The skill in `solidworks-bridge/` gives it the
**method**: how to work with the bridge, the pitfalls already found, and the advice of an
experienced designer on parts, mates, tree organization, drawings and 3D printing.

**Install it.** Without it the AI can use the bridge, but the quality of the models and
drawings is lower: it mates with distances instead of contact and symmetry, places details
on empty space, and trusts verdicts that are known to be wrong.

## Claude Code

Copy the folder `solidworks-bridge` into your personal skills folder:

```
%USERPROFILE%\.claude\skills\solidworks-bridge\
```

so that the file is `%USERPROFILE%\.claude\skills\solidworks-bridge\SKILL.md`. Claude Code
loads it in the next session.

## Other AI clients

If your client has no skills, add the text of `solidworks-bridge/SKILL.md` to the project or
system instructions, and keep the `references/` files where the AI can read them.

## Versions

`SKILL.md` says the skill version and the bridge version it was tested with. When you update
the bridge, update the skill too.
