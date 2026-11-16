using System;
using SolidWorks.Interop.sldworks;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE TREE AND THE GRAPHICS TURNED OFF WHILE A PLAN RUNS, and put back as they
    /// were (library 0.180.0, 29/10/2026; asked by the owner to measure it before anything
    /// else that makes the bridge faster).
    ///
    /// ── WHY ────────────────────────────────────────────────────────────
    ///
    /// The guide, IFeatureManager~EnableFeatureTree.html: «Temporarily disabling the
    /// FeatureManager design tree should increase performance», and while it is off
    /// the tree «may become out of date with what is actually in the model».
    /// IFeatureManager~EnableFeatureTreeWindow.html: to be used with EnableFeatureTree.
    /// IModelView~EnableGraphicsUpdate.html: it «affects whether to refresh the model
    /// view during a selection, such as IEntity::Select4 or IFeature::Select2».
    /// xCAD (MIT, src/SolidWorks/Utils/UiFreeze.cs) turns the same three off and puts
    /// them back in Dispose. Nobody published a time for them on SOLIDWORKS 2021.
    ///
    /// ── HOW ────────────────────────────────────────────────────────────
    ///
    /// ONLY WHAT IS ON IS TURNED OFF, AND ONLY WHAT WAS TURNED OFF IS PUT BACK: a tree
    /// the user switched off stays theirs. When the tree comes back it is updated
    /// (IFeatureManager.UpdateFeatureTree), and when the graphics come back the view is
    /// redrawn (IModelView.GraphicsRedraw): whoever looks at SOLIDWORKS after the plan
    /// must see the part as it is, not as it was before the first step.
    ///
    /// OFF UNLESS ASKED until it is measured: `OCE_UI_FREEZE` set to `1`, `true` or
    /// `on` turns it on. Unset, nothing changes.
    ///
    /// ── THE CYCLE OF DRAWINGS (library 0.188.0, 30/10/2026) ───────────────
    ///
    /// A cycle of 15 drawings through the bridge (new drawing + second sheet + save, then
    /// close; scenario t45) fell 11 times in 11 runs alternated on 30/10/2026, painting a
    /// drawing view (`gfxSgSheetView_c::removeOleBitmapNode` → `uoBillboardi_c::make`); with
    /// `OCE_UI_FREEZE=1` it reached 15 drawings 3 times in 3 (RAG note 95, par. 12.12). To tell
    /// which part does it, the value can also name one: `graphics` turns off only the
    /// graphics of the view (EnableGraphicsUpdate), `tree` only the tree and its window.
    ///
    /// Measured the same day (RAG note 95, par. 12.14): `graphics` 0 falls in 4 runs, `tree`
    /// 4 in 4. Over the day, the graphics left on 17 falls in 19 runs, turned off 0 in 12
    /// (Fisher, two-sided p = 6.45e-07). The cause is a sketch picture (a logo) in the sheet
    /// format of the drawing: SOLIDWORKS 2021 SP5 falls with it also without the bridge, and
    /// never without it (RAG note 97). Kept off by default by decision of the owner, who
    /// replaces the picture with lines in a block; `graphics` is the way round it.
    /// </summary>
    public sealed class UiFreeze : IDisposable
    {
        /// <summary>The environment variable that turns it on.</summary>
        public const string Variable = "OCE_UI_FREEZE";

        /// <summary>What `OCE_UI_FREEZE` turns off.</summary>
        [Flags]
        internal enum Parts
        {
            None = 0,
            Tree = 1,
            Graphics = 2,
            All = Tree | Graphics,
        }

        private IFeatureManager _features;
        private IModelView _view;
        private bool _tree, _window, _graphics;

        private UiFreeze() { }

        /// <summary>Whether `OCE_UI_FREEZE` asks for it: `1`, `true`, `on`, `graphics` or `tree`.</summary>
        public static bool Wanted()
        {
            return Wanted(System.Environment.GetEnvironmentVariable(Variable));
        }

        /// <summary><see cref="Wanted()"/> with the value given: the contract suite reads it without the environment.</summary>
        internal static bool Wanted(string setting)
        {
            return PartsOf(setting) != Parts.None;
        }

        /// <summary>
        /// The parts a value of `OCE_UI_FREEZE` turns off: all three for `1`, `true` or `on`,
        /// the graphics of the view for `graphics`, the tree and its window for `tree`, none
        /// for anything else. Pure, so the contract suite reads it.
        /// </summary>
        internal static Parts PartsOf(string setting)
        {
            if (string.IsNullOrWhiteSpace(setting)) return Parts.None;
            setting = setting.Trim();
            if (setting == "1" ||
                string.Equals(setting, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(setting, "on", StringComparison.OrdinalIgnoreCase))
                return Parts.All;
            if (string.Equals(setting, "graphics", StringComparison.OrdinalIgnoreCase)) return Parts.Graphics;
            if (string.Equals(setting, "tree", StringComparison.OrdinalIgnoreCase)) return Parts.Tree;
            return Parts.None;
        }

        /// <summary>
        /// Turns off what is on for <paramref name="document"/>, among the parts
        /// `OCE_UI_FREEZE` names. Null when it is not wanted, when there is no document,
        /// or when nothing was on: then there is nothing to put back.
        /// </summary>
        public static UiFreeze Begin(IModelDoc2 document)
        {
            var parts = PartsOf(System.Environment.GetEnvironmentVariable(Variable));
            if (document == null || parts == Parts.None) return null;
            var freeze = new UiFreeze();
            if ((parts & Parts.Tree) != 0)
            {
                try { freeze._features = document.FeatureManager; } catch { }
            }
            if (freeze._features != null)
            {
                try
                {
                    if (freeze._features.EnableFeatureTree)
                    {
                        freeze._features.EnableFeatureTree = false;
                        freeze._tree = true;
                    }
                }
                catch { }
                try
                {
                    if (freeze._features.EnableFeatureTreeWindow)
                    {
                        freeze._features.EnableFeatureTreeWindow = false;
                        freeze._window = true;
                    }
                }
                catch { }
            }
            if ((parts & Parts.Graphics) != 0)
            {
                try { freeze._view = document.ActiveView as IModelView; } catch { }
            }
            if (freeze._view != null)
            {
                try
                {
                    if (freeze._view.EnableGraphicsUpdate)
                    {
                        freeze._view.EnableGraphicsUpdate = false;
                        freeze._graphics = true;
                    }
                }
                catch { }
            }
            return freeze._tree || freeze._window || freeze._graphics ? freeze : null;
        }

        /// <summary>Puts back what <see cref="Begin"/> turned off, in the reverse order, and only that.</summary>
        public void Dispose()
        {
            if (_graphics)
            {
                try { _view.EnableGraphicsUpdate = true; } catch { }
                // IModelView~GraphicsRedraw.html: «if the array is empty, then the entire
                // graphics window is redrawn». Not IModelDoc2.GraphicsRedraw2, which
                // IModelDoc2~GraphicsRedraw2.html calls obsolete, superseded by this one.
                try { _view.GraphicsRedraw(new int[0]); } catch { }
            }
            if (_window)
            {
                try { _features.EnableFeatureTreeWindow = true; } catch { }
            }
            if (_tree)
            {
                try { _features.EnableFeatureTree = true; } catch { }
                try { _features.UpdateFeatureTree(); } catch { }
            }
            _graphics = _window = _tree = false;
        }
    }
}
