using System;

namespace Shared
{
    /// <summary>
    /// WHEN SOLIDWORKS HAS GONE AWAY, THE LATER CHECKS ARE NOT RED:
    /// THEY WERE NOT DONE (entry K17).
    ///
    /// ── WHAT IT COST NOT TO HAVE IT ──────────────────────────────────────
    ///
    /// On 23/9/2026, the 15:36 run. After an hour and a half of clean work —
    /// 312 green checks — SOLIDWORKS stopped producing documents:
    /// `NewDocument` answered `null`, and from there on it answered `null`
    /// to everyone. The bench already had entry F12, so it SAID what it had not
    /// managed to prepare, for each one:
    ///
    ///     NO   detail_enlarges   SOLIDWORKS did not produce the part
    ///     NO   loft_is_not_a_cone      SOLIDWORKS did not produce the part
    ///     NO   plate_bounding_box     SOLIDWORKS did not produce the part
    ///     ...
    ///
    /// Nine red cases in a row, and then the process got stuck inside the
    /// next COM call — `Responding=False` on the SOLIDWORKS process, no modal
    /// window open, title without a document name. The bench stayed
    /// there indefinitely: it has no deadline on the calls to the
    /// library, only on the bridge.
    ///
    /// TWO DEFECTS, NOT ONE:
    ///
    ///   1. The report said **red** where the true fact was «not
    ///      measured». Whoever reads «detail_enlarges: NO» goes to look at
    ///      the case — which was right — instead of looking at SOLIDWORKS.
    ///
    ///   2. The run **kept going**. Forty minutes of checks against a
    ///      dead program, and the final verdict would have been a list of
    ///      reds that say nothing about anything.
    ///
    /// The message of F12 already ended with «Look how many documents are
    /// still open, and run again». It was the right remedy written in the
    /// wrong place: **to a human who may not be there**. Here the bench does it by itself.
    ///
    /// ── WHY THREE, AND WHY IN A ROW ────────────────────────────────────────
    ///
    /// A new part that is not born ONE time can be the piece: a
    /// missing document template, a full disk, a case that left
    /// SOLIDWORKS in a strange way and the next one recovers. Three in a row
    /// without a single one being born in between is no longer the piece: it is the
    /// program.
    ///
    /// «In a row» is the part that counts, and that is why
    /// <see cref="Succeeded"/> resets. A count that does not reset confuses three
    /// failures scattered over eight hundred checks — which are normal noise —
    /// with three failures stuck together, which are a program that is no longer there.
    ///
    /// ── WHY IT LIVES HERE AND NOT IN THE BENCH ─────────────────────────────
    ///
    /// Because a guard that nobody has ever seen trip is not a
    /// guard. To see it trip in the bench one would have to make
    /// SOLIDWORKS die on command; in `Shared` it is also compiled by
    /// `Oce.Solid.Agent.ContractTests`, where SOLIDWORKS is not there at all, and there it is made to
    /// fail on purpose every time the checks are run — in a tenth of a
    /// second, without CAD.
    /// </summary>
    internal sealed class SolidWorksSentinel
    {
        private readonly int _threshold;
        private int _inARow;

        /// <summary>
        /// How many there were AT THE MOMENT IT TRIPPED. It is not the same thing
        /// as <see cref="InARow"/>: that one is a live counter, and whoever
        /// asked for the report after something called
        /// <see cref="Succeeded"/> would read «0 attempts in a row gone
        /// empty» in a message that announces a death.
        ///
        /// It happened on 23/9/2026, in the check written for the report: the
        /// check passed all the same because it looked for the digit «3», which was in the
        /// sentence — it was the THRESHOLD. A right number in the wrong place
        /// that covers a wrong number: the check now demands «3
        /// attempts», not «somewhere there is a 3».
        /// </summary>
        private int _whenItFires;

        /// <summary>
        /// How many failures IN A ROW before saying that the program is no
        /// longer there. Three in the bench; the constructor takes it because the checks
        /// must be able to test the threshold one and the threshold five as well —
        /// otherwise one would be testing the number three, not the rule.
        /// </summary>
        internal SolidWorksSentinel(int threshold)
        {
            if (threshold < 1)
                throw new ArgumentOutOfRangeException(
                    "threshold",
                    "a sentinel that trips after zero failures trips before "
                    + "looking: the threshold is at least 1.");
            _threshold = threshold;
        }

        /// <summary>True when the program must be considered dead. It does not go
        /// back to false: from here on nothing is measured any more, it is said what
        /// was not measured.</summary>
        internal bool HasGone { get; private set; }

        /// <summary>How many failures in a row at this moment. It is needed by the
        /// message: «three in a row» is a fact, «it is dead» is a
        /// conclusion, and the reader is entitled to the fact.</summary>
        internal int InARow { get { return _inARow; } }

        /// <summary>The threshold it was built with, for the message.</summary>
        internal int Threshold { get { return _threshold; } }

        /// <summary>A document was born: the count starts again from zero.</summary>
        internal void Succeeded()
        {
            _inARow = 0;
        }

        /// <summary>
        /// A document was not born. It returns true when this is the failure
        /// that trips the sentinel — or a later one.
        /// </summary>
        internal bool Failed()
        {
            _inARow++;
            if (_inARow >= _threshold && !HasGone)
            {
                HasGone = true;
                _whenItFires = _inARow;
            }
            return HasGone;
        }

        /// <summary>
        /// What to say to whoever reads the report. It carries the NUMBERS: how many in a row
        /// and on which threshold. «SOLIDWORKS does not respond» without numbers cannot be
        /// told apart from an opinion.
        /// </summary>
        internal string Report()
        {
            int howMany = HasGone ? _whenItFires : _inARow;
            return "SOLIDWORKS NO LONGER PRODUCES DOCUMENTS: " + howMany
                 + " attempts in a row came to nothing (the threshold is " + _threshold
                 + "). The checks that follow are not red — THEY WERE NOT DONE. "
                 + "Close SOLIDWORKS, reopen it and restart the run.";
        }
    }
}
