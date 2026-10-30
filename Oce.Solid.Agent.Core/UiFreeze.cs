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
    /// </summary>
    public sealed class UiFreeze : IDisposable
    {
        /// <summary>The environment variable that turns it on.</summary>
        public const string Variable = "OCE_UI_FREEZE";

        private IFeatureManager _features;
        private IModelView _view;
        private bool _tree, _window, _graphics;

        private UiFreeze() { }

        /// <summary>Whether `OCE_UI_FREEZE` asks for it: on only for `1`, `true` or `on`.</summary>
        public static bool Wanted()
        {
            return Wanted(System.Environment.GetEnvironmentVariable(Variable));
        }

        /// <summary><see cref="Wanted()"/> with the value given: the contract suite reads it without the environment.</summary>
        internal static bool Wanted(string setting)
        {
            if (string.IsNullOrWhiteSpace(setting)) return false;
            setting = setting.Trim();
            return setting == "1" ||
                   string.Equals(setting, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(setting, "on", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Turns off what is on for <paramref name="document"/>. Null when it is not
        /// wanted, when there is no document, or when nothing was on: then there is
        /// nothing to put back.
        /// </summary>
        public static UiFreeze Begin(IModelDoc2 document)
        {
            if (document == null || !Wanted()) return null;
            var freeze = new UiFreeze();
            try { freeze._features = document.FeatureManager; } catch { }
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
            try { freeze._view = document.ActiveView as IModelView; } catch { }
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
