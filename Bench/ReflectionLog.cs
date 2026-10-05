using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Oce.Solid.Agent.Bench
{
    /// <summary>
    /// THE LOOKUPS BY NAME OF THE BENCH THAT WOULD COME BACK NULL IN SILENCE
    /// (entry W3.1-bis.2, item 3, the bench side).
    ///
    /// Most lookups of the bench dereference what they find at once, and a
    /// renamed target is an exception nobody can miss. A few read with `?.` or
    /// ask for a type with throwOnError false, and go on with null: the
    /// schemas of the mutations, the catalogue count of the header, the
    /// signatures of the state. Renamed on the library side only, they would
    /// give fewer mutations, a count of zero, a refusal that names the wrong
    /// reason. The static net of Oce.Solid.Agent.ContractTests (ReflectionTests) resolves
    /// their literal names, but a member name resolves as soon as ANY type
    /// declares it, and 14 of the 58 direct member names of `Tests` and `Bench`
    /// are declared by more than one type (measured on 1/10/2026: `Name` by
    /// four, `All` and `Schema` by two). This log asks the type that is
    /// really there, on the path the run takes, and the run says at its end
    /// whether any came back null.
    ///
    /// Compiled by the bench and, as a linked file, by Oce.Solid.Agent.ContractTests, which
    /// sees it red on a fake name at every run.
    /// </summary>
    internal sealed class ReflectionLog
    {
        internal static readonly ReflectionLog Global = new ReflectionLog();

        /// <summary>What Type.GetField(name) and its siblings use without flags.</summary>
        internal const BindingFlags PublicMembers = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

        // A named list type: the obsolete-call scanner of Oce.Solid.Agent.ContractTests reads
        // the receiver of `_misses.Add` from the declaration of the field.
        private sealed class MissList : List<string> { }

        private readonly MissList _misses = new MissList();

        internal int Lookups { get; private set; }

        internal IList<string> Misses
        {
            get { return _misses.AsReadOnly(); }
        }

        internal MethodInfo Method(Type owner, string name, BindingFlags flags)
        {
            return Note(owner == null ? null : owner.GetMethod(name, flags), "method", owner, name);
        }

        internal PropertyInfo Property(Type owner, string name, BindingFlags flags)
        {
            return Note(owner == null ? null : owner.GetProperty(name, flags), "property", owner, name);
        }

        internal FieldInfo Field(Type owner, string name, BindingFlags flags)
        {
            return Note(owner == null ? null : owner.GetField(name, flags), "field", owner, name);
        }

        internal FieldInfo Field(Type owner, string name)
        {
            return Field(owner, name, PublicMembers);
        }

        /// <summary>A type by its full name in an assembly, null (and logged) when it is not there.</summary>
        internal Type TypeIn(Assembly assembly, string fullName)
        {
            var found = assembly == null ? null : assembly.GetType(fullName, false);
            if (found != null) { Lookups++; return found; }
            AssemblyName where = assembly == null ? null : assembly.GetName();
            Miss("type " + fullName + " in " + (where == null ? "(no assembly)" : where.Name));
            return null;
        }

        /// <summary>Null when every lookup found its target, otherwise what came back null.</summary>
        internal string Verdict()
        {
            if (_misses.Count == 0) return null;
            return _misses.Count + " of " + Lookups + " lookups came back null: " +
                   string.Join("; ", _misses.Distinct().Take(8));
        }

        private T Note<T>(T found, string kind, Type owner, string name) where T : class
        {
            if (found != null) { Lookups++; return found; }
            Miss(kind + " " + (owner == null ? "(no type)" : owner.FullName) + "." + name);
            return null;
        }

        private void Miss(string what)
        {
            Lookups++;
            _misses.Add(what);
        }
    }
}
