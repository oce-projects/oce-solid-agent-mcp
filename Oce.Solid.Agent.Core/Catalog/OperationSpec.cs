using System.Collections.Generic;

namespace Oce.Solid.Agent.Core.Catalog
{
    /// <summary>An entry of the catalog: what can be asked, and how it is written.</summary>
    public sealed class OperationSpec
    {
        /// <summary>The exact name, the one that goes in `build`.</summary>
        public string Name;

        /// <summary>Which family it belongs to.</summary>
        public string Family;

        /// <summary>A single line: it is what the model sees first.</summary>
        public string Summary;

        /// <summary>
        /// The long text. **It is the real interface towards the model**, much
        /// more than the schema: it is here that it is said when NOT to use
        /// an operation, in what units the numbers are, and what must already be
        /// in the document. It is touched with the same care as code.
        /// </summary>
        public string Description;

        /// <summary>The schema of the arguments.</summary>
        public Node Schema;

        /// <summary>What must be in the document for it to make sense. May be missing.</summary>
        public string Precondition;

        /// <summary>
        /// WHAT DOCUMENT IT RUNS ON: «part», «assembly», «drawing» — or more
        /// than one separated by a slash, «any» if it runs on all,
        /// «none» if it does not touch the active document (entry Z-D0-ter).
        ///
        /// ── WHY THE FAMILY IS NOT ENOUGH ─────────────────────────────
        ///
        /// The family is an approximation, and `checks` shows it
        /// on its own: `title_block` wants a DRAWING, `interference_check` an
        /// ASSEMBLY, and the other six a PART. Three different documents under
        /// the same label.
        ///
        /// As long as it was not written, that fact lived in the head of whoever
        /// wrote the operation and in a sentence of prose that no check
        /// reads. **An operation that declares what document it runs on is
        /// an operation that the model does not try on the wrong document**,
        /// and trying it costs a turn.
        /// </summary>
        public string RequestedDocument;

        /// <summary>
        /// A REAL CALL, in JSON, taken from a GREEN bench case
        /// (entry Z-G1).
        ///
        /// ── WHY THE SCHEMA IS NOT ENOUGH ─────────────────────────────
        ///
        /// A schema says which fields are lawful; **it does not say which
        /// combination builds a part**. They are two different things, and the
        /// second is learned only by seeing it.
        ///
        /// ── AND IT IS NOT INVENTED ───────────────────────────────────
        ///
        /// Every example is copied from a step that the bench executed and
        /// measured. A hand-written example is a promise: this is
        /// a fact. It is also the reason why many operations do not
        /// have one — their cases use selectors that live only in the bench
        /// (`@face(normal=up)`), and an example that the client cannot
        /// copy is worse than no example.
        ///
        /// ── THE RISK, DECLARED ───────────────────────────────────────
        ///
        /// **An example becomes a mould.** If 200x120x15 is here,
        /// that part will show up everywhere. The numbers of an example are
        /// ARBITRARY: the fields and their shape count, not the values.
        /// </summary>
        public string Example;

        /// <summary>
        /// Was it built and MEASURED inside SOLIDWORKS?
        ///
        /// IT DOES NOT END UP IN THE JSON, AND THAT IS THE POINT. As long as it is false the operation
        /// is not exported, so the model does not see it and cannot ask for it.
        /// «Proven» here means one precise thing: somebody ran the
        /// operation on a real part and looked at what came out — not that
        /// the code compiles.
        ///
        /// It sits next to the code that executes, and not in a distant file, because
        /// a promise written elsewhere forgets to be kept.
        /// </summary>
        public bool Tested;
    }
}
