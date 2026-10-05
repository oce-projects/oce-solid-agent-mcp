using System;

namespace Shared
{
    /// <summary>
    /// I COULD NOT MEASURE. Not «I measured and the part was wrong».
    ///
    /// ── WHY A TYPE IS NEEDED, AND THE MESSAGE WAS NOT ENOUGH (entry K17) ─────
    ///
    /// The bench has long told the two things apart in the REPORT: `NO` is a
    /// red check, `!!` is a bench error, and they have different exit codes
    /// because they call for two different reactions — the first is looked at in the
    /// code of the product, the second in the bench.
    ///
    /// But the preparation of a declared case failed inside a
    /// `catch (Exception)` that marked it red and nothing else, and so on 23/9/2026 the
    /// report said:
    ///
    ///     NO   detail_enlarges   SOLIDWORKS did not produce the part
    ///
    /// on a well-written case, when the fact was that SOLIDWORKS no longer opened
    /// documents. Whoever reads that line goes to read the CASE — and it is the
    /// wrong place.
    ///
    /// A type of its own lets the `catch` make the difference without reading the
    /// text of the message. **Comparing messages is the same trap one
    /// line further on**: the day someone rewrites the sentence, the
    /// `catch` goes back to marking red and nobody notices.
    ///
    /// It stays an <see cref="InvalidOperationException"/> so that whoever catches it
    /// that way — and there are those who do — keeps catching it.
    /// </summary>
    internal class PreparationFailed : InvalidOperationException
    {
        internal PreparationFailed(string message) : base(message) { }
    }
}
