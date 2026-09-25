using ColossalFramework.UI;
using UnityEngine;

namespace MyFirstMod
{
    // One of the four workspaces (Directive 03 section 3.1). Builds its controls
    // once and redraws only when the snapshot version, or its own UI state, has
    // changed (WO-42).
    public abstract class WorkspaceView
    {
        public const float Width = 836f;
        public const float Height = 424f;

        protected readonly BondMarketWindow Window;
        public readonly UIPanel Root;
        private int _renderedVersion = -1;
        private bool _uiDirty = true;

        protected WorkspaceView(BondMarketWindow window, UIComponent parent)
        {
            Window = window;
            Root = parent.AddUIComponent<UIPanel>();
            Root.size = new Vector2(Width, Height);
            Root.relativePosition = Vector3.zero;
        }

        public abstract string Title { get; }

        public void Build() { BuildControls(); }

        protected abstract void BuildControls();
        protected abstract void Render(EngineSnapshot s);

        // The player changed something on this workspace (a slider, a scroll).
        protected void Invalidate() { _uiDirty = true; }

        // Returns true if it drew.
        public bool Refresh(EngineSnapshot s)
        {
            if (s == null) return false;
            if (!_uiDirty && s.Version == _renderedVersion) return false;
            _renderedVersion = s.Version;
            _uiDirty = false;
            Render(s);
            return true;
        }

        public void Show(bool visible)
        {
            if (Root.isVisible != visible) Root.isVisible = visible;
            if (visible) _uiDirty = true;
        }

        protected static void Submit(EngineCommand command)
        {
            BondMarketEngine e = BondMarketEngine.Instance;
            if (e != null) e.Submit(command);
        }
    }
}
