using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno.Api
{
    /// <summary>
    /// The answer of <see cref="ApiCatalog.Search"/> and
    /// <see cref="ApiCatalog.Signature"/>: text for the model, and whether it
    /// is an error (a name that does not exist). Fields, not an overload with
    /// an out parameter: the bench and the contracts read the library by
    /// reflection, and a plain type is the one they can read without guessing.
    /// </summary>
    public sealed class ApiAnswer
    {
        public string Text;
        public bool IsError;
    }

    /// <summary>
    /// WHAT THE API IS, READ FROM THE INTEROP ITSELF (entry U1.1).
    ///
    /// ── WHY REFLECTION AND NOT A LIST ──────────────────────────────────
    ///
    /// The interop loaded in this process (<c>typeof(ISldWorks).Assembly</c>)
    /// is the one the calls will run against: it cannot be out of date the way
    /// a documentation page or a copied list can. 463 interfaces with members,
    /// 14,928 members on SOLIDWORKS 2021 SP5.1, plus the enums of
    /// <c>swconst</c>. <c>swmotionstudy</c> is added when present, read only
    /// (for entry S2): it is searched and described, never counted.
    ///
    /// ── THE FOUR OLD INTERFACES ARE NOT EXPOSED ────────────────────────
    ///
    /// <c>IModelDoc</c>, <c>IBody</c>, <c>IFace</c> and <c>IComponent</c> do not
    /// have a single member name their new sisters lack (counted by name on the
    /// inventory, and re-checked by a contract with
    /// <see cref="InterfaceGaps"/>). Showing them would only double every
    /// search result with a version that SOLIDWORKS keeps for old macros. A
    /// search names them and points to the sister; a signature request answers
    /// "old interface: use ..." and does NOT print the old signature.
    ///
    /// ── WHAT A SIGNATURE CARD SAYS, AND WHAT IT REFUSES TO GUESS ───────
    ///
    /// Every overload with the real types; property or method; the <c>&amp;</c>
    /// out parameters by name; the typed <c>I…</c> variant next to the untyped
    /// one; the Void flag (a Void creator gives nothing back: the new feature
    /// is only found from the before/after snapshot); the numbered family
    /// (<c>X</c>, <c>X2</c>, <c>X3</c>…) with the last one marked "latest by
    /// number, not verified" — a higher number is a newer signature, not a
    /// measured one.
    ///
    /// Two things are NOT derived, on purpose:
    ///   * the enum of an <c>Int32</c> parameter. The signature says Int32;
    ///     <c>AlignFromEnum</c> can be guessed, <c>Type</c> cannot. The link
    ///     comes only from the hand-written map in <c>api_knowledge.json</c>;
    ///     without it the card says "enum not tabulated".
    ///   * the meaning of array indices. Writing the "classic" order of an
    ///     array nobody measured would give a wrong and plausible number (the
    ///     von Mises minimum read at the maximum's slot). Measured indices come
    ///     from the knowledge file; anything else is "index not tabulated".
    ///
    /// Knowledge (state, enum map, indices, measured type of an Object
    /// parameter, source) is passed in as JSON text: the bridge embeds
    /// <c>Strumenti/api_knowledge.json</c>, the contracts read the same file.
    /// </summary>
    public static class ApiCatalog
    {
        // ─────────────────────────────────────────────────────────────────
        //  Vocabulary
        // ─────────────────────────────────────────────────────────────────

        /// <summary>The states a member can have in api_knowledge.json.</summary>
        public static readonly string[] States =
        {
            "forbidden", "confirm", "gray", "never_probed", "probed_read", "probed_modifies",
        };

        private static readonly string[] EntryFields =
        {
            "state", "why", "source", "enums", "indices", "measured_types",
        };

        public const string VoidFlag =
            "returns Void: the new feature can only be found from the snapshot " +
            "(before/after), never from the return value";

        public const string LatestByNumber = "latest by number, not verified";
        public const string EnumNotTabulated = "enum not tabulated";
        public const string IndexNotTabulated = "index not tabulated";
        public const string OldInterface = "old interface: use ";

        /// <summary>
        /// The hidden interfaces and their sisters, in the order to try them.
        /// </summary>
        private static readonly KeyValuePair<string, string[]>[] HiddenList =
        {
            new KeyValuePair<string, string[]>("IModelDoc", new[] { "IModelDoc2", "IModelDocExtension" }),
            new KeyValuePair<string, string[]>("IBody", new[] { "IBody2" }),
            new KeyValuePair<string, string[]>("IFace", new[] { "IFace2" }),
            new KeyValuePair<string, string[]>("IComponent", new[] { "IComponent2" }),
        };

        /// <summary>Old interface -> the sisters that replace it.</summary>
        public static Dictionary<string, string[]> HiddenInterfaces()
        {
            return HiddenList.ToDictionary(p => p.Key, p => (string[])p.Value.Clone(), StringComparer.Ordinal);
        }

        // ─────────────────────────────────────────────────────────────────
        //  The index, built once
        // ─────────────────────────────────────────────────────────────────

        private sealed class Index
        {
            /// <summary>sldworks interfaces named I* with at least one method, hidden ones included.</summary>
            public readonly Dictionary<string, Type> Interfaces = new Dictionary<string, Type>(StringComparer.Ordinal);
            public readonly Dictionary<string, Type> InterfacesCi = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            /// <summary>swmotionstudy interfaces, read only, never counted.</summary>
            public readonly Dictionary<string, Type> Motion = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            /// <summary>swconst enums (and swmotionstudy enums, flagged by assembly).</summary>
            public readonly Dictionary<string, Type> Enums = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            public Assembly MotionAssembly;
            public string MotionNote;
        }

        private static readonly Lazy<Index> TheIndex = new Lazy<Index>(Build, true);

        private static Type[] TypesOf(Assembly a)
        {
            try { return a.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null).ToArray(); }
        }

        private static bool CountsAsInterface(Type t)
        {
            // The same filter as Controlli\api-inventory.ps1: an interface,
            // named with a capital I, that declares at least one method. The
            // coclass interfaces (ModelDoc2, Body2...) declare nothing, and the
            // D...Events interfaces are event sinks, not something to call.
            return t.IsInterface && t.Name.Length > 1 && t.Name[0] == 'I' && t.GetMethods().Length > 0;
        }

        private static Index Build()
        {
            var ix = new Index();
            foreach (var t in TypesOf(typeof(ISldWorks).Assembly))
            {
                if (!CountsAsInterface(t)) continue;
                ix.Interfaces[t.Name] = t;
                ix.InterfacesCi[t.Name] = t;
            }
            foreach (var t in TypesOf(typeof(swMateAlign_e).Assembly))
                if (t.IsEnum) ix.Enums[t.Name] = t;

            // swmotionstudy: next to sldworks, when it is there. Read only.
            try
            {
                var here = typeof(ISldWorks).Assembly.Location;
                var dir = string.IsNullOrEmpty(here) ? null : Path.GetDirectoryName(here);
                var file = dir == null ? null : Path.Combine(dir, "SolidWorks.Interop.swmotionstudy.dll");
                if (file != null && File.Exists(file))
                {
                    ix.MotionAssembly = Assembly.LoadFrom(file);
                    foreach (var t in TypesOf(ix.MotionAssembly))
                    {
                        if (CountsAsInterface(t)) ix.Motion[t.Name] = t;
                        else if (t.IsEnum && !ix.Enums.ContainsKey(t.Name)) ix.Enums[t.Name] = t;
                    }
                }
                else ix.MotionNote = "swmotionstudy not found next to the sldworks interop";
            }
            catch (Exception ex) { ix.MotionNote = "swmotionstudy not loaded: " + ex.Message; }
            return ix;
        }

        private static bool IsHidden(string iface)
        {
            return HiddenList.Any(p => string.Equals(p.Key, iface, StringComparison.Ordinal));
        }

        private static string[] SistersOf(string iface)
        {
            foreach (var p in HiddenList)
                if (string.Equals(p.Key, iface, StringComparison.Ordinal)) return p.Value;
            return new string[0];
        }

        // ─────────────────────────────────────────────────────────────────
        //  Formatting, and the inventory the contract compares
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// One member in the inventory format:
        /// <c>Interface.Member(Type Name, Type&amp; Name) -> Ret</c>. The same
        /// format Controlli\api-inventory.ps1 writes from PowerShell; the
        /// contract ApiSignatureMatchesInventory compares the two.
        /// </summary>
        public static string Format(Type iface, MethodInfo m)
        {
            var ps = m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name);
            return iface.Name + "." + m.Name + "(" + string.Join(", ", ps) + ") -> " + m.ReturnType.Name;
        }

        /// <summary>
        /// Every member of every sldworks interface, hidden ones included,
        /// formatted, sorted ordinal. 14,928 lines on 2021 SP5.1.
        /// </summary>
        public static string[] InventoryLines()
        {
            var lines = new List<string>();
            foreach (var t in TheIndex.Value.Interfaces.Values)
                foreach (var m in t.GetMethods()) lines.Add(Format(t, m));
            var a = lines.ToArray();
            Array.Sort(a, StringComparer.Ordinal);
            return a;
        }

        /// <summary>Every swconst enum: <c>Name: a=0, b=1</c>, in Enum.GetNames order.</summary>
        public static string[] EnumInventoryLines()
        {
            var lines = new List<string>();
            var cst = typeof(swMateAlign_e).Assembly;
            foreach (var t in TheIndex.Value.Enums.Values)
            {
                if (t.Assembly != cst) continue;
                lines.Add(t.Name + ": " + string.Join(", ", EnumPairs(t).Select(p => p.Key + "=" + p.Value)));
            }
            var a = lines.ToArray();
            Array.Sort(a, StringComparer.Ordinal);
            return a;
        }

        private static List<KeyValuePair<string, long>> EnumPairs(Type e)
        {
            var names = Enum.GetNames(e);
            var values = Enum.GetValues(e);
            var list = new List<KeyValuePair<string, long>>();
            for (int i = 0; i < names.Length; i++)
                list.Add(new KeyValuePair<string, long>(names[i], Convert.ToInt64(values.GetValue(i), CultureInfo.InvariantCulture)));
            return list;
        }

        /// <summary>Members that api_search and api_signature show: all minus the four old interfaces.</summary>
        public static int ExposedMemberCount()
        {
            return TheIndex.Value.Interfaces.Values.Where(t => !IsHidden(t.Name)).Sum(t => t.GetMethods().Length);
        }

        /// <summary>Members of the four old interfaces, named but never shown.</summary>
        public static int HiddenMemberCount()
        {
            return TheIndex.Value.Interfaces.Values.Where(t => IsHidden(t.Name)).Sum(t => t.GetMethods().Length);
        }

        /// <summary>
        /// Member names of <paramref name="hidden"/> that none of
        /// <paramref name="sisters"/> has. Empty means hiding it loses nothing.
        /// Public so the contract can also show it biting (IMassProperty has
        /// names IMassProperty2 lacks).
        /// </summary>
        public static string[] InterfaceGaps(string hidden, params string[] sisters)
        {
            var ix = TheIndex.Value;
            Type h;
            if (!ix.Interfaces.TryGetValue(hidden, out h)) return new[] { "(no interface " + hidden + ")" };
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in sisters)
            {
                Type st;
                if (!ix.Interfaces.TryGetValue(s, out st)) return new[] { "(no interface " + s + ")" };
                foreach (var m in st.GetMethods()) names.Add(m.Name);
            }
            return h.GetMethods().Select(m => m.Name).Where(n => !names.Contains(n))
                    .Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray();
        }

        /// <summary>For every hidden interface, the names its sisters lack ("IModelDoc.X").</summary>
        public static string[] HiddenInterfaceGaps()
        {
            var all = new List<string>();
            foreach (var p in HiddenList)
                all.AddRange(InterfaceGaps(p.Key, p.Value).Select(n => p.Key + "." + n));
            return all.ToArray();
        }

        // ─────────────────────────────────────────────────────────────────
        //  Knowledge (api_knowledge.json)
        // ─────────────────────────────────────────────────────────────────

        private sealed class Entry
        {
            public string Key;
            public string State;
            public string Why;
            public string Source;
            public Dictionary<string, string> Enums = new Dictionary<string, string>(StringComparer.Ordinal);
            public Dictionary<string, Dictionary<string, string>> Indices =
                new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            public Dictionary<string, string> MeasuredTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        private sealed class Knowledge
        {
            public readonly Dictionary<string, Entry> Members = new Dictionary<string, Entry>(StringComparer.Ordinal);
            public readonly List<string> Problems = new List<string>();
        }

        private static readonly object KnowledgeLock = new object();
        private static string _lastJson;
        private static Knowledge _lastKnowledge;

        private static Knowledge Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new Knowledge();
            lock (KnowledgeLock)
            {
                if (ReferenceEquals(json, _lastJson) || json == _lastJson) return _lastKnowledge;
            }
            var k = new Knowledge();
            try
            {
                var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var root = ser.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null) { k.Problems.Add("the knowledge file is not a JSON object"); }
                else
                {
                    object membersObj;
                    var members = root.TryGetValue("members", out membersObj) ? membersObj as Dictionary<string, object> : null;
                    if (members == null) k.Problems.Add("the knowledge file has no \"members\" object");
                    else
                        foreach (var kv in members)
                        {
                            var d = kv.Value as Dictionary<string, object>;
                            if (d == null) { k.Problems.Add(kv.Key + ": not an object"); continue; }
                            var e = new Entry { Key = kv.Key };
                            e.State = Str(d, "state");
                            e.Why = Str(d, "why");
                            e.Source = Str(d, "source");
                            foreach (var f in d.Keys)
                                if (!EntryFields.Contains(f)) k.Problems.Add(kv.Key + ": unknown field \"" + f + "\"");
                            ReadFlat(d, "enums", e.Enums, kv.Key, k.Problems);
                            ReadFlat(d, "measured_types", e.MeasuredTypes, kv.Key, k.Problems);
                            object idx;
                            if (d.TryGetValue("indices", out idx) && idx != null)
                            {
                                var id = idx as Dictionary<string, object>;
                                if (id == null) k.Problems.Add(kv.Key + ": \"indices\" is not an object");
                                else
                                    foreach (var target in id)
                                    {
                                        var slots = target.Value as Dictionary<string, object>;
                                        if (slots == null)
                                        {
                                            k.Problems.Add(kv.Key + ": indices of \"" + target.Key +
                                                           "\" must be an object {\"0-2\": \"meaning\"}");
                                            continue;
                                        }
                                        var m = new Dictionary<string, string>(StringComparer.Ordinal);
                                        foreach (var s in slots) m[s.Key] = Convert.ToString(s.Value, CultureInfo.InvariantCulture);
                                        e.Indices[target.Key] = m;
                                    }
                            }
                            k.Members[kv.Key] = e;
                        }
                }
            }
            catch (Exception ex) { k.Problems.Add("the knowledge file does not parse: " + ex.Message); }
            lock (KnowledgeLock) { _lastJson = json; _lastKnowledge = k; }
            return k;
        }

        private static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
        }

        private static void ReadFlat(Dictionary<string, object> d, string key, Dictionary<string, string> into,
                                     string member, List<string> problems)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return;
            var m = v as Dictionary<string, object>;
            if (m == null) { problems.Add(member + ": \"" + key + "\" is not an object"); return; }
            foreach (var kv in m) into[kv.Key] = Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// What is wrong in a knowledge file: a member that does not exist, a
        /// state outside the six, an enum map on a parameter the member does
        /// not have or to an enum that does not exist, a measured type on a
        /// parameter that is not declared Object, indices on a target that is
        /// neither "return" nor a parameter, a missing source. Empty = clean.
        /// </summary>
        public static string[] KnowledgeProblems(string json)
        {
            var k = Parse(json);
            var problems = new List<string>(k.Problems);
            foreach (var e in k.Members.Values)
            {
                var methods = Resolve(e.Key);
                if (methods == null)
                {
                    problems.Add(e.Key + ": no such member in the interop");
                    continue;
                }
                if (e.State == null || !States.Contains(e.State))
                    problems.Add(e.Key + ": state \"" + e.State + "\" is not one of " + string.Join("/", States));
                if (string.IsNullOrWhiteSpace(e.Source))
                    problems.Add(e.Key + ": no source (memory, RAG note, code or bench case)");
                var pars = new Dictionary<string, Type>(StringComparer.Ordinal);
                foreach (var m in methods)
                    foreach (var p in m.GetParameters())
                        if (p.Name != null) pars[p.Name] = p.ParameterType;
                foreach (var kv in e.Enums)
                {
                    Type pt;
                    if (!pars.TryGetValue(kv.Key, out pt))
                        problems.Add(e.Key + ": enum map on \"" + kv.Key + "\", which is not a parameter (" +
                                     string.Join(", ", pars.Keys) + ")");
                    else if (!IsIntegral(pt))
                        problems.Add(e.Key + ": enum map on \"" + kv.Key + "\", declared " + pt.Name + ", not an integer");
                    Type en;
                    if (!TheIndex.Value.Enums.TryGetValue(kv.Value ?? "", out en) || !string.Equals(en.Name, kv.Value, StringComparison.Ordinal))
                        problems.Add(e.Key + ": enum map \"" + kv.Key + "\" -> \"" + kv.Value + "\", which is not an enum of swconst");
                }
                foreach (var kv in e.MeasuredTypes)
                {
                    Type pt;
                    if (!pars.TryGetValue(kv.Key, out pt))
                        problems.Add(e.Key + ": measured type on \"" + kv.Key + "\", which is not a parameter (" +
                                     string.Join(", ", pars.Keys) + ")");
                    else if (pt != typeof(object) && pt != typeof(object).MakeByRefType())
                        problems.Add(e.Key + ": measured type on \"" + kv.Key + "\", declared " + pt.Name +
                                     ": measured types are only for parameters declared Object");
                }
                foreach (var target in e.Indices.Keys)
                    if (target != "return" && !pars.ContainsKey(target))
                        problems.Add(e.Key + ": indices on \"" + target + "\", neither \"return\" nor a parameter");
            }
            return problems.ToArray();
        }

        private static bool IsIntegral(Type t)
        {
            if (t.IsByRef) t = t.GetElementType();
            return t == typeof(int) || t == typeof(short) || t == typeof(long) || t == typeof(uint) || t == typeof(ushort);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Resolving a name
        // ─────────────────────────────────────────────────────────────────

        private static Type FindInterface(string name, out bool motion)
        {
            motion = false;
            var ix = TheIndex.Value;
            Type t;
            if (ix.InterfacesCi.TryGetValue(name, out t)) return t;
            if (ix.Motion.TryGetValue(name, out t)) { motion = true; return t; }
            return null;
        }

        /// <summary>
        /// The methods a knowledge key or a signature request names:
        /// <c>IBody2.Check3</c> (the property: its get_ and set_),
        /// <c>IBody2.get_Check3</c> (that accessor only) or a method.
        /// Null when nothing matches. Hidden interfaces resolve too.
        /// </summary>
        private static List<MethodInfo> Resolve(string key)
        {
            var dot = key == null ? -1 : key.IndexOf('.');
            if (dot <= 0 || dot == key.Length - 1) return null;
            bool motion;
            var t = FindInterface(key.Substring(0, dot), out motion);
            if (t == null) return null;
            var found = MembersNamed(t, key.Substring(dot + 1));
            return found.Count == 0 ? null : found;
        }

        private static List<MethodInfo> MembersNamed(Type t, string member)
        {
            var all = t.GetMethods();
            var exact = all.Where(m => string.Equals(m.Name, member, StringComparison.Ordinal)).ToList();
            if (exact.Count > 0) return exact;
            var prop = all.Where(m => string.Equals(m.Name, "get_" + member, StringComparison.Ordinal) ||
                                      string.Equals(m.Name, "set_" + member, StringComparison.Ordinal)).ToList();
            if (prop.Count > 0) return prop;
            // Case-insensitive, last: the model often writes "check3".
            var ci = all.Where(m => string.Equals(m.Name, member, StringComparison.OrdinalIgnoreCase)).ToList();
            if (ci.Count > 0) return ci;
            return all.Where(m => string.Equals(m.Name, "get_" + member, StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(m.Name, "set_" + member, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static string BareName(MethodInfo m)
        {
            if (m.IsSpecialName && (m.Name.StartsWith("get_", StringComparison.Ordinal) ||
                                    m.Name.StartsWith("set_", StringComparison.Ordinal)))
                return m.Name.Substring(4);
            return m.Name;
        }

        private static bool IsAccessor(MethodInfo m)
        {
            return m.IsSpecialName && (m.Name.StartsWith("get_", StringComparison.Ordinal) ||
                                       m.Name.StartsWith("set_", StringComparison.Ordinal));
        }

        // ─────────────────────────────────────────────────────────────────
        //  api_signature
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// The card of one name: <c>Interface.Member</c>, an interface, an enum
        /// of swconst, or a bare member name (answered when it is unique).
        /// </summary>
        public static ApiAnswer Signature(string name, string knowledgeJson)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0)
                return Error("Give a name: «IBody2.Check3», «IFeatureManager.FeatureExtrusion3», " +
                             "an interface («IEdge») or an enum of swconst («swMateAlign_e»).");
            var k = Parse(knowledgeJson);
            var ix = TheIndex.Value;

            var dot = name.IndexOf('.');
            if (dot > 0)
            {
                var ifaceName = name.Substring(0, dot);
                var member = name.Substring(dot + 1);
                if (IsHiddenCi(ifaceName, out var hidden)) return OldInterfaceAnswer(hidden, member);
                bool motion;
                var t = FindInterface(ifaceName, out motion);
                if (t == null)
                {
                    Type en;
                    if (ix.Enums.TryGetValue(ifaceName, out en)) return new ApiAnswer { Text = EnumCard(en, member) };
                    return Error("There is no interface «" + ifaceName + "» in the interop." + Suggest(ifaceName));
                }
                var methods = MembersNamed(t, member);
                if (methods.Count == 0)
                    return Error("«" + t.Name + "» has no member «" + member + "»." + Suggest(member, t));
                return new ApiAnswer { Text = MemberCard(t, methods, k, motion) };
            }

            // No dot: hidden interface, interface, enum, or a bare member name.
            if (IsHiddenCi(name, out var h)) return OldInterfaceAnswer(h, null);
            {
                bool motion;
                var t = FindInterface(name, out motion);
                if (t != null) return new ApiAnswer { Text = InterfaceCard(t, k, motion) };
                Type en;
                if (ix.Enums.TryGetValue(name, out en)) return new ApiAnswer { Text = EnumCard(en, null) };
            }
            var owners = ix.Interfaces.Values.Where(t => !IsHidden(t.Name))
                .Where(t => MembersNamed(t, name).Count > 0).ToList();
            if (owners.Count == 1)
                return new ApiAnswer { Text = MemberCard(owners[0], MembersNamed(owners[0], name), k, false) };
            if (owners.Count > 1)
                return new ApiAnswer
                {
                    Text = "«" + name + "» exists on " + owners.Count + " interfaces; ask for one of them:\n" +
                           string.Join("\n", owners.OrderBy(t => t.Name, StringComparer.Ordinal)
                                                   .Select(t => "  " + t.Name + "." + BareName(MembersNamed(t, name)[0])))
                };
            return Error("Nothing is called «" + name + "»." + Suggest(name));
        }

        private static bool IsHiddenCi(string name, out string hidden)
        {
            foreach (var p in HiddenList)
                if (string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)) { hidden = p.Key; return true; }
            hidden = null;
            return false;
        }

        private static ApiAnswer OldInterfaceAnswer(string hidden, string member)
        {
            var sisters = SistersOf(hidden);
            var ix = TheIndex.Value;
            string target = sisters[0];
            string targetMember = null;
            if (member != null)
                foreach (var s in sisters)
                {
                    var found = MembersNamed(ix.Interfaces[s], member);
                    if (found.Count > 0) { target = s; targetMember = s + "." + BareName(found[0]); break; }
                }
            var sb = new StringBuilder();
            sb.Append(member == null ? hidden : hidden + "." + member).Append(": ")
              .Append(OldInterface).Append(target);
            if (targetMember != null) sb.Append(" (ask for «").Append(targetMember).Append("»)");
            sb.Append(".\n").Append(hidden).Append(" is not exposed: every member name it has also exists on ")
              .Append(string.Join(" or ", sisters))
              .Append(" (a contract checks it), so hiding it loses nothing. SOLIDWORKS keeps it for old macros.");
            if (member != null && targetMember == null)
                sb.Append("\n«").Append(member).Append("» is not a member of ").Append(hidden).Append(" either.");
            return new ApiAnswer { Text = sb.ToString() };
        }

        private static string MemberCard(Type t, List<MethodInfo> methods, Knowledge k, bool motion)
        {
            var sb = new StringBuilder();
            var bare = BareName(methods[0]);
            bool property = methods.All(IsAccessor);
            var key = t.Name + "." + bare;

            sb.Append(key).Append(" — ");
            if (property)
            {
                bool get = methods.Any(m => m.Name.StartsWith("get_", StringComparison.Ordinal));
                bool set = methods.Any(m => m.Name.StartsWith("set_", StringComparison.Ordinal));
                sb.Append("property, ").Append(get && set ? "read/write" : get ? "read-only" : "write-only");
            }
            else sb.Append(methods.Count > 1 ? "method, " + methods.Count + " overloads" : "method");
            if (motion) sb.Append(" — swmotionstudy, READ ONLY here (entry S2)");
            sb.Append('\n');

            // A property request also shows its accessors when the model asked
            // for just one of them ("IBody2.get_Check3").
            if (property)
            {
                var both = t.GetMethods().Where(m => IsAccessor(m) && BareName(m) == bare).ToList();
                if (both.Count > methods.Count) methods = both;
            }

            foreach (var m in methods.OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                sb.Append("  ").Append(Format(t, m));
                if (property) sb.Append(m.Name.StartsWith("get_", StringComparison.Ordinal) ? "   (get)" : "   (set)");
                sb.Append('\n');
            }

            // Knowledge: the member entry, then accessor-level entries.
            var entries = new List<Entry>();
            Entry e;
            if (k.Members.TryGetValue(key, out e)) entries.Add(e);
            foreach (var m in methods)
                if (m.Name != bare && k.Members.TryGetValue(t.Name + "." + m.Name, out e)) entries.Add(e);

            foreach (var m in methods.OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                var pars = m.GetParameters();
                bool voidCreator = !IsAccessor(m) && m.ReturnType == typeof(void);
                if (voidCreator) sb.Append("  ").Append(VoidFlag).Append('\n');
                if (pars.Length > 0)
                {
                    if (methods.Count > 1) sb.Append("  parameters of ").Append(m.Name).Append(":\n");
                    else sb.Append("  parameters:\n");
                    foreach (var p in pars) AppendParameter(sb, p, entries);
                    var outs = pars.Where(p => p.ParameterType.IsByRef).Select(p => p.Name).ToList();
                    if (outs.Count > 0)
                        sb.Append("  out parameters (&), by name: ").Append(string.Join(", ", outs)).Append('\n');
                }
                AppendReturnIndices(sb, m, entries);
            }

            // The typed I-variant, or the untyped one next to an I-variant.
            foreach (var line in Variants(t, bare)) sb.Append("  ").Append(line).Append('\n');

            // The numbered family.
            var family = Family(t, bare);
            if (family.Count > 1)
            {
                var latest = family.Last();
                sb.Append("  family: ").Append(string.Join(", ", family)).Append('\n');
                sb.Append("    ").Append(latest).Append(": ").Append(LatestByNumber).Append('\n');
                if (!string.Equals(latest, bare, StringComparison.Ordinal))
                    sb.Append("    you asked for ").Append(bare).Append(": a higher number exists (").Append(latest)
                      .Append("). Which one works is a measurement, not the number.\n");
            }

            // State and source.
            if (entries.Count == 0)
                sb.Append("  state: never_probed (not in api_knowledge.json: nobody has measured it here)\n");
            else
                foreach (var en in entries)
                {
                    sb.Append("  state").Append(en.Key == key ? "" : " of " + en.Key).Append(": ").Append(en.State ?? "?").Append('\n');
                    if (!string.IsNullOrWhiteSpace(en.Why)) sb.Append("    why: ").Append(en.Why).Append('\n');
                    if (!string.IsNullOrWhiteSpace(en.Source)) sb.Append("    source: ").Append(en.Source).Append('\n');
                }
            return sb.ToString().TrimEnd('\n');
        }

        private static void AppendParameter(StringBuilder sb, ParameterInfo p, List<Entry> entries)
        {
            var pt = p.ParameterType;
            sb.Append("    ").Append(p.Name).Append("  ").Append(pt.Name);
            if (pt.IsByRef) sb.Append("  OUT (by reference)");
            sb.Append('\n');

            string enumName = null;
            foreach (var e in entries)
                if (e.Enums.TryGetValue(p.Name ?? "", out enumName)) break;
            Type en = null;
            if (enumName != null) TheIndex.Value.Enums.TryGetValue(enumName, out en);
            if (en != null)
            {
                sb.Append("      enum ").Append(en.Name).Append(" (hand-written map in api_knowledge.json):\n");
                foreach (var line in EnumValueLines(en)) sb.Append("        ").Append(line).Append('\n');
            }
            else if (IsIntegral(pt))
                sb.Append("      ").Append(EnumNotTabulated)
                  .Append(": the signature says ").Append(pt.Name.TrimEnd('&'))
                  .Append(", and which enum (if any) is a hand-written fact nobody wrote yet\n");

            var elem = pt.IsByRef ? pt.GetElementType() : pt;
            if (elem == typeof(object))
            {
                string measured = null;
                foreach (var e in entries)
                    if (e.MeasuredTypes.TryGetValue(p.Name ?? "", out measured)) break;
                sb.Append("      measured type: ")
                  .Append(measured ?? "none (declared Object; an array passed as object[] may create a phantom feature — " +
                                      "the type must be measured before a call)")
                  .Append('\n');
                AppendIndices(sb, p.Name, entries, "      ");
            }
            else if (elem.IsArray || (pt.IsByRef && elem == typeof(double)))
                AppendIndices(sb, p.Name, entries, "      ");
        }

        private static void AppendReturnIndices(StringBuilder sb, MethodInfo m, List<Entry> entries)
        {
            var r = m.ReturnType;
            if (r == typeof(object) || r.IsArray)
            {
                sb.Append("  return ").Append(r.Name).Append(":\n");
                AppendIndices(sb, "return", entries, "    ");
            }
        }

        private static void AppendIndices(StringBuilder sb, string target, List<Entry> entries, string indent)
        {
            Dictionary<string, string> slots = null;
            foreach (var e in entries)
                if (e.Indices.TryGetValue(target ?? "", out slots)) break;
            if (slots == null || slots.Count == 0)
            {
                sb.Append(indent).Append("indices: ").Append(IndexNotTabulated)
                  .Append(" (if it is an array: never write the \"classic\" order, measure it)\n");
                return;
            }
            sb.Append(indent).Append("indices (measured):\n");
            foreach (var s in slots) sb.Append(indent).Append("  ").Append(s.Key).Append(" = ").Append(s.Value).Append('\n');
        }

        private static IEnumerable<string> EnumValueLines(Type en)
        {
            foreach (var g in EnumPairs(en).GroupBy(p => p.Value))
            {
                var names = g.Select(p => p.Key).ToList();
                var line = g.Key.ToString(CultureInfo.InvariantCulture) + " = " + string.Join(" | ", names);
                if (names.Count > 1)
                    line += "   AMBIGUOUS: " + names.Count + " names share this value, and they may mean " +
                            "opposite things (read which one the code means)";
                yield return line;
            }
        }

        private static List<string> Variants(Type t, string bare)
        {
            var list = new List<string>();
            var all = t.GetMethods();
            Func<string, IEnumerable<MethodInfo>> named = n => all.Where(m => BareName(m) == n);
            if (bare.Length > 1 && bare[0] == 'I' && char.IsUpper(bare[1]))
            {
                foreach (var m in named(bare.Substring(1)))
                    list.Add("untyped variant: " + Format(t, m));
            }
            foreach (var m in named("I" + bare))
                list.Add("typed variant: " + Format(t, m));
            return list;
        }

        /// <summary>X, X2, X3... on the same interface, sorted by number.</summary>
        private static List<string> Family(Type t, string bare)
        {
            var root = bare.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            if (root.Length == 0) return new List<string> { bare };
            var names = t.GetMethods().Select(BareName).Distinct()
                .Where(n => n.StartsWith(root, StringComparison.Ordinal) &&
                            n.Substring(root.Length).All(char.IsDigit))
                .ToList();
            return names.OrderBy(n => n.Length == root.Length ? 0 : int.Parse(n.Substring(root.Length), CultureInfo.InvariantCulture))
                        .ToList();
        }

        private static string InterfaceCard(Type t, Knowledge k, bool motion)
        {
            var names = t.GetMethods().Select(BareName).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
            var sb = new StringBuilder();
            sb.Append(t.Name).Append(" — interface, ").Append(t.GetMethods().Length).Append(" members (")
              .Append(names.Count).Append(" names, properties counted once)");
            if (motion) sb.Append(" — swmotionstudy, READ ONLY here (entry S2)");
            sb.Append('\n');
            sb.Append("Ask «").Append(t.Name).Append(".<member>» for a signature.\n");
            sb.Append(string.Join(", ", names));
            return sb.ToString();
        }

        private static string EnumCard(Type en, string value)
        {
            var sb = new StringBuilder();
            var pairs = EnumPairs(en);
            sb.Append(en.Name).Append(" — enum");
            if (en.Assembly != typeof(swMateAlign_e).Assembly) sb.Append(" (swmotionstudy)");
            sb.Append(", ").Append(pairs.Count).Append(" names");
            if (pairs.Count > 0)
                sb.Append(", from ").Append(pairs[0].Key).Append('=').Append(pairs[0].Value)
                  .Append(" to ").Append(pairs[pairs.Count - 1].Key).Append('=').Append(pairs[pairs.Count - 1].Value);
            sb.Append('\n');
            foreach (var line in EnumValueLines(en))
                if (value == null || line.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
                    sb.Append("  ").Append(line).Append('\n');
            return sb.ToString().TrimEnd('\n');
        }

        // ─────────────────────────────────────────────────────────────────
        //  api_search
        // ─────────────────────────────────────────────────────────────────

        public const int DefaultLimit = 40;
        public const int MaxLimit = 200;

        /// <summary>
        /// Members, interfaces and enums whose name contains the query
        /// (case-insensitive). <c>Interface.part</c> restricts to interfaces
        /// whose name contains the part before the dot. Exposed members come
        /// as real signatures; members of the four old interfaces come as one
        /// line that names the sister, never as a signature.
        /// </summary>
        public static ApiAnswer Search(string query, int limit, string knowledgeJson)
        {
            query = (query ?? "").Trim();
            if (query.Length < 2)
                return Error("Give at least two characters: a piece of a member name («SewRefSurface»), " +
                             "«Interface.piece» («IEdge.Adjacent») or an enum («swMateAlign»).");
            if (limit <= 0) limit = DefaultLimit;
            if (limit > MaxLimit) limit = MaxLimit;
            var k = Parse(knowledgeJson);
            var ix = TheIndex.Value;

            string ifacePart = null, memberPart = query;
            var dot = query.IndexOf('.');
            if (dot > 0) { ifacePart = query.Substring(0, dot); memberPart = query.Substring(dot + 1); }

            var signatures = new List<string>();
            var old = new List<string>();
            var interfaces = new List<string>();
            var motion = new List<string>();

            Func<string, string, bool> has = (s, part) =>
                part.Length == 0 || s.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

            foreach (var t in ix.Interfaces.Values.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                if (ifacePart != null && !has(t.Name, ifacePart)) continue;
                if (ifacePart == null && has(t.Name, query) && !IsHidden(t.Name)) interfaces.Add(t.Name);
                foreach (var m in t.GetMethods().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    if (!has(BareName(m), memberPart)) continue;
                    if (IsHidden(t.Name))
                    {
                        var line = OldLine(t.Name, BareName(m));
                        if (!old.Contains(line)) old.Add(line);
                        continue;
                    }
                    var sig = Format(t, m);
                    Entry e;
                    if (k.Members.TryGetValue(t.Name + "." + m.Name, out e) ||
                        k.Members.TryGetValue(t.Name + "." + BareName(m), out e))
                        sig += "   [" + e.State + "]";
                    signatures.Add(sig);
                }
            }
            foreach (var t in ix.Motion.Values.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                if (ifacePart != null && !has(t.Name, ifacePart)) continue;
                foreach (var m in t.GetMethods().OrderBy(x => x.Name, StringComparer.Ordinal))
                    if (has(BareName(m), memberPart)) motion.Add("[swmotionstudy, read only] " + Format(t, m));
            }

            var enums = new List<string>();
            if (ifacePart == null)
                foreach (var en in ix.Enums.Values.OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    if (has(en.Name, query)) { enums.Add("enum " + en.Name); continue; }
                    foreach (var p in EnumPairs(en))
                        if (has(p.Key, query)) enums.Add("enum value " + en.Name + "." + p.Key + " = " + p.Value);
                }

            // Exact member name first: "AddMate5" puts AddMate5 before AddMate5Ex.
            signatures = signatures
                .OrderBy(s => MemberOf(s).Equals(memberPart, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(s => s, StringComparer.Ordinal).ToList();

            int total = signatures.Count + old.Count + interfaces.Count + motion.Count + enums.Count;
            if (total == 0)
                return Error("Nothing in the interop contains «" + query + "»." + Suggest(memberPart));

            var sb = new StringBuilder();
            sb.Append(signatures.Count).Append(" exposed signatures");
            if (interfaces.Count > 0) sb.Append(", ").Append(interfaces.Count).Append(" interfaces");
            if (enums.Count > 0) sb.Append(", ").Append(enums.Count).Append(" enum names");
            if (motion.Count > 0) sb.Append(", ").Append(motion.Count).Append(" swmotionstudy signatures (read only)");
            if (old.Count > 0) sb.Append(", ").Append(old.Count).Append(" on old interfaces (not exposed)");
            sb.Append(" for «").Append(query).Append("».");
            int shown = 0;
            Action<string, List<string>> block = (title, list) =>
            {
                if (list.Count == 0) return;
                sb.Append("\n\n").Append(title).Append(':');
                foreach (var l in list)
                {
                    if (shown >= limit) break;
                    sb.Append("\n  ").Append(l);
                    shown++;
                }
            };
            block("signatures", signatures);
            block("interfaces", interfaces);
            block("enums", enums);
            block("swmotionstudy (read only, entry S2)", motion);
            block("old interfaces, NOT exposed", old);
            if (shown < total)
                sb.Append("\n\n… ").Append(total - shown).Append(" more not shown (limit ").Append(limit)
                  .Append("). Narrow it: «Interface.piece», or a longer piece of the name.");
            sb.Append("\n\nFor the full card of one of them: api_signature with «Interface.Member».");
            return new ApiAnswer { Text = sb.ToString() };
        }

        private static string OldLine(string hidden, string member)
        {
            var ix = TheIndex.Value;
            foreach (var s in SistersOf(hidden))
                if (MembersNamed(ix.Interfaces[s], member).Count > 0)
                    return hidden + "." + member + " — " + OldInterface + s + "." + member;
            return hidden + "." + member + " — " + OldInterface + string.Join(" or ", SistersOf(hidden));
        }

        private static string MemberOf(string signature)
        {
            var open = signature.IndexOf('(');
            var dot = signature.IndexOf('.');
            if (open < 0 || dot < 0 || dot > open) return "";
            var n = signature.Substring(dot + 1, open - dot - 1);
            return n.StartsWith("get_", StringComparison.Ordinal) || n.StartsWith("set_", StringComparison.Ordinal) ? n.Substring(4) : n;
        }

        private static string Suggest(string piece, Type within = null)
        {
            if (string.IsNullOrWhiteSpace(piece) || piece.Length < 3) return "";
            var ix = TheIndex.Value;
            var probe = piece.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            if (probe.Length < 3) probe = piece;
            IEnumerable<Type> where = within != null ? new[] { within } : ix.Interfaces.Values.Where(t => !IsHidden(t.Name));
            var near = where.SelectMany(t => t.GetMethods().Select(m => t.Name + "." + BareName(m)))
                .Where(n => n.IndexOf(probe, StringComparison.OrdinalIgnoreCase) >= 0)
                .Distinct().OrderBy(n => n, StringComparer.Ordinal).Take(8).ToList();
            if (within == null)
                near.AddRange(ix.Interfaces.Keys.Where(n => !IsHidden(n) &&
                              n.IndexOf(probe, StringComparison.OrdinalIgnoreCase) >= 0).Take(4));
            return near.Count == 0 ? " Try api_search with a shorter piece of the name."
                                   : " Near: " + string.Join(", ", near) + ".";
        }

        private static ApiAnswer Error(string text)
        {
            return new ApiAnswer { Text = text, IsError = true };
        }
    }
}
