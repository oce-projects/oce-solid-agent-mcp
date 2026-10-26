using System.Collections.Generic;
using System.Threading.Tasks;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// Where the steps of the plan come from.
    ///
    /// It is the ONLY boundary invented in the whole extraction: eleven files out of
    /// twelve need no interface at all, because they only talk to
    /// SOLIDWORKS. `PlanExecutor` instead talked to the OpenPDM server,
    /// and without this boundary it would have carried the PDM along inside
    /// the library that exists so as not to depend on it.
    ///
    /// Whoever implements it decides where the steps come from: a server, a JSON
    /// file, an in-memory list for a check. The executor does not know it and must
    /// not know it.
    /// </summary>
    public interface IPlanSource
    {
        /// <summary>
        /// The next step, given the document state now.
        ///
        /// IT IS ASKED EVERY TIME instead of downloading the whole plan because the
        /// check has to be redone: between the first step and the last minutes can pass,
        /// and in between the document may have changed.
        ///
        /// **A failure is NOT reported as «finished»: it is raised.** See the
        /// note on <see cref="NextStep"/>.
        /// </summary>
        Task<NextStep> NextAsync(Dictionary<string, object> docState);

        /// <summary>How the step just executed went.</summary>
        Task OutcomeAsync(
            int order, bool succeeded, Dictionary<string, object> measurements, string error);
    }

    /// <summary>
    /// A source that never looks at the state it is given: the executor does
    /// not read the document for it (brief R2).
    ///
    /// A separate interface and not a member of <see cref="IPlanSource"/>: a
    /// new member would break every source already compiled against this
    /// library (the add-in has its own), and the executor would refuse to load
    /// them. Up to library 0.153.1 the state was read before every step for a
    /// source that threw it away (the bridge's own, «The state is not
    /// needed»): measured on 26/10/2026, it was one of the two full readings
    /// of every step.
    /// </summary>
    public interface IPlanSourceWithoutState : IPlanSource
    {
    }

    /// <summary>
    /// The answer to «what is the next step»: either there is one, or the plan is
    /// finished.
    ///
    /// WHY A TYPE INSTEAD OF A `null`.
    /// The obvious shape would be to make <see
    /// cref="IPlanSource.NextAsync"/> return `null` to say «finished», and save
    /// this type. It is not done, and it is a choice against a precise defect: an
    /// adapter that swallows a network exception and returns `null`
    /// would report **«plan completed»** for a plan interrupted halfway.
    /// No error, no warning, and a part that looks finished to whoever is looking.
    ///
    /// With an explicit <see cref="Done"/>, «I received nothing» and «there is
    /// nothing left to do» do not have the same shape, and the first cannot be
    /// written by distraction in place of the second.
    ///
    /// Rule for whoever implements the source: **a failure is raised, not
    /// translated.** If the network drops, the exception must reach the executor, which
    /// writes it in <see cref="PlanExecutor.Result.Error"/> leaving
    /// `Completed` false.
    /// </summary>
    public class NextStep
    {
        /// <summary>True when the plan has reached the end.</summary>
        public bool Done;

        /// <summary>The step to execute. Null if <see cref="Done"/>.</summary>
        public PlanStep Step;

        public static NextStep Fine()
        {
            return new NextStep { Done = true };
        }

        public static NextStep This(PlanStep planStep)
        {
            return new NextStep { Done = false, Step = planStep };
        }
    }

    /// <summary>A step of the plan: what to do, with which arguments.</summary>
    public class PlanStep
    {
        /// <summary>The order number, counting from 1.</summary>
        public int Order;

        /// <summary>The name of the operation, as in the catalogue.</summary>
        public string Operation;

        /// <summary>The arguments of the operation.</summary>
        public Dictionary<string, object> Arguments;

        /// <summary>
        /// What the step must measure (brief R16), as the plan wrote it next to its
        /// arguments; null when it says nothing. See <see cref="StepExpectations"/>.
        /// </summary>
        public Dictionary<string, object> Expect;

        /// <summary>A line in Italian on what the step does, for the log.</summary>
        public string Summary;
    }
}
