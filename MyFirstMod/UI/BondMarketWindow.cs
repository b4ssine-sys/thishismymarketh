using System;
using ColossalFramework.UI;
using UnityEngine;

namespace MyFirstMod
{
    // Directive 03 section 3.1: four workspaces (Treasury, Borrow, Invest, Risk)
    // with the activity feed along the bottom. Reads only the engine's published
    // snapshot and places orders on its queue (WO-40); redraws only what changed
    // (WO-42); remembers its position, last workspace and text size (WO-39).
    public sealed class BondMarketWindow : UIPanel
    {
        public static BondMarketWindow Instance;

        public const int TreasuryIndex = 0;
        public const int BorrowIndex = 1;
        public const int InvestIndex = 2;
        public const int RiskIndex = 3;

        private const float WindowWidth = 860f;
        private const float WindowHeight = 566f;
        private const float ContentTop = 76f;
        private const float RefreshSeconds = 4f;

        private readonly UIComponent[] _built = new UIComponent[4];
        private UILabel _title;
        private BoundLabel _status;
        private readonly UIButton[] _tabs = new UIButton[4];
        private UIPanel _content;
        private readonly WorkspaceView[] _views = new WorkspaceView[4];
        private readonly BoundLabel[] _feed = new BoundLabel[3];
        private BriefingView _briefing;

        private int _active;
        private int _lastFeedVersion = -1;
        private int _lastSeenResult;
        private int _pendingSequence;
        private float _timer;
        private Vector3 _lastSavedPosition;
        private float _positionSettle;

        public override void Start()
        {
            base.Start();
            Instance = this;
            backgroundSprite = Theme.PanelSprite;
            size = new Vector2(WindowWidth, WindowHeight);
            canFocus = true;
            isInteractive = true;
            Build();
            PlaceFromPrefs();
            _active = Mathf.Clamp(UiPrefs.Workspace, 0, 3);
            SelectWorkspace(_active);
            isVisible = false;
        }

        private void Build()
        {
            UIPanel titleBar = AddUIComponent<UIPanel>();
            titleBar.size = new Vector2(WindowWidth, 40f);
            titleBar.relativePosition = Vector3.zero;
            UIDragHandle drag = titleBar.AddUIComponent<UIDragHandle>();
            drag.size = titleBar.size;
            drag.relativePosition = Vector3.zero;
            drag.target = this;
            _title = Widgets.Label(titleBar, 12f, 8f, 260f, 24f, 1.05f, Loc.Get("panel.title"), null);
            _status = Widgets.Figure(titleBar, 276f, 8f, 530f, 24f, 0.75f, "The outcome of your latest order.");
            _status.Color(Theme.Accent, 1);
            UIButton close = titleBar.AddUIComponent<UIButton>();
            close.size = new Vector2(32f, 32f);
            close.relativePosition = new Vector3(WindowWidth - 40f, 4f);
            close.normalBgSprite = "buttonclose";
            close.hoveredBgSprite = "buttonclosehover";
            close.pressedBgSprite = "buttonclosepressed";
            close.eventClick += delegate(UIComponent c, UIMouseEventParameter p) { Hide(); };
            _built[0] = titleBar;

            UIPanel tabBar = AddUIComponent<UIPanel>();
            tabBar.size = new Vector2(WindowWidth, 32f);
            tabBar.relativePosition = new Vector3(0f, 40f);
            string[] names = { "Treasury", "Borrow", "Invest", "Risk" };
            string[] tips =
            {
                "Your rating and what decides it, cash runway, payments due and one recommendation.",
                "Price and issue a bond; see every payment due over the next three years.",
                "Other issuers' bonds, and the city's holdings.",
                "Rate exposure, swaps, the market regime and settings."
            };
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                _tabs[i] = Widgets.Button(tabBar, names[i], 12f + i * 128f, 2f, 122f, 28f, tips[i],
                    delegate(UIComponent c, UIMouseEventParameter p) { SelectWorkspace(index); });
            }
            _built[1] = tabBar;

            _content = AddUIComponent<UIPanel>();
            _content.size = new Vector2(WorkspaceView.Width, WorkspaceView.Height);
            _content.relativePosition = new Vector3(12f, ContentTop);
            _views[TreasuryIndex] = new TreasuryView(this, _content);
            _views[BorrowIndex] = new BorrowView(this, _content);
            _views[InvestIndex] = new InvestView(this, _content);
            _views[RiskIndex] = new RiskView(this, _content);
            for (int i = 0; i < 4; i++) _views[i].Build();
            _built[2] = _content;

            UIPanel feed = Widgets.Card(this, 12f, ContentTop + WorkspaceView.Height + 6f, WorkspaceView.Width, 58f);
            for (int i = 0; i < _feed.Length; i++)
            {
                _feed[i] = Widgets.Figure(feed, 8f, 2f + i * 18f, WorkspaceView.Width - 16f, 18f, 0.68f,
                    "Latest alert, order outcome and citizen bond-market activity.");
                _feed[i].Label.wordWrap = false;
            }
            _built[3] = feed;
        }

        private void PlaceFromPrefs()
        {
            UIView view = GetUIView();
            float sw = view != null ? view.fixedWidth : 1920f;
            float sh = view != null ? view.fixedHeight : 1080f;
            float x = UiPrefs.GetFloat("X", (sw - WindowWidth) / 2f);
            float y = UiPrefs.GetFloat("Y", (sh - WindowHeight) / 2f);
            x = Mathf.Clamp(x, 0f, Math.Max(0f, sw - WindowWidth));
            y = Mathf.Clamp(y, 0f, Math.Max(0f, sh - WindowHeight));
            absolutePosition = new Vector3(x, y);
            _lastSavedPosition = absolutePosition;
        }

        public void Toggle()
        {
            if (isVisible) { Hide(); return; }
            Show();
            BringToFront();
            if (!UiPrefs.BriefingSeen) ShowBriefing();
            Redraw(true);
        }

        public void SelectWorkspace(int index)
        {
            _active = Mathf.Clamp(index, 0, 3);
            UiPrefs.Workspace = _active;
            for (int i = 0; i < 4; i++)
            {
                _views[i].Show(i == _active);
                _tabs[i].normalBgSprite = i == _active ? Theme.ButtonFocused : Theme.ButtonSprite;
            }
            Redraw(true);
        }

        public void OpenWorkspace(int index) { SelectWorkspace(index); }

        internal WorkspaceView ViewAt(int index) { return _views[index]; }
        internal int ActiveWorkspace { get { return _active; } }
        internal string StatusText { get { return _status.Label.text; } }

        // Draw now, as the frame loop would while the window is open.
        internal void RedrawNow() { Redraw(true); }

        // From the Treasury recommendation or the briefing: the ticket, on a
        // template (or -1 to keep the current one).
        public void OpenBorrow(int template)
        {
            BorrowView borrow = (BorrowView)_views[BorrowIndex];
            if (template >= 0) borrow.SelectTemplate(template);
            SelectWorkspace(BorrowIndex);
        }

        public void ShowBriefing()
        {
            if (_briefing != null) _briefing.Close();
            _briefing = new BriefingView(this);
        }

        internal void BriefingClosed(bool openTicket)
        {
            _briefing = null;
            UiPrefs.BriefingSeen = true;
            if (openTicket) OpenBorrow(IssueTemplates.EmergencyNote);
        }

        public void ShowPending(string text)
        {
            BondMarketEngine e = BondMarketEngine.Instance;
            _pendingSequence = e != null ? e.Snapshot.LastProcessedSequence + 1 : 0;
            _status.Text(text + " (runs on the next simulation tick)");
        }

        // WO-39: text size applies on rebuild, so every label picks it up.
        public void SetTextScale(float scale)
        {
            UiPrefs.TextScale = scale;
            for (int i = 0; i < _built.Length; i++)
                if (_built[i] != null) UnityEngine.Object.Destroy(_built[i].gameObject);
            if (_briefing != null) { _briefing.Close(); _briefing = null; }
            Build();
            SelectWorkspace(_active);
        }

        public override void Update()
        {
            base.Update();
            if (Input.GetKeyDown(KeyCode.B) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
                Toggle();
            if (!isVisible) return;

            RememberPosition();
            _timer += Time.deltaTime;
            Redraw(_timer >= RefreshSeconds);
        }

        // WO-42: a workspace redraws only when the snapshot version changed (or its
        // own controls did); the four-second timer only re-checks, it does not
        // force a redraw.
        private void Redraw(bool timerExpired)
        {
            if (timerExpired) _timer = 0f;
            BondMarketEngine e = BondMarketEngine.Instance;
            if (e == null)
            {
                _status.Text(Loc.Get("status.notready"));
                return;
            }
            EngineSnapshot s = e.Snapshot;
            _views[_active].Refresh(s);
            if (s.Version != _lastFeedVersion)
            {
                _lastFeedVersion = s.Version;
                RenderFeed(s);
                ReportResults(s);
            }
        }

        private void RenderFeed(EngineSnapshot s)
        {
            string[] lines = FeedModel.Lines(s);
            for (int i = 0; i < _feed.Length; i++)
                _feed[i].Text(i < lines.Length ? lines[i] : "");
        }

        private void ReportResults(EngineSnapshot s)
        {
            for (int i = 0; i < s.RecentResults.Length; i++)
            {
                CommandResult r = s.RecentResults[i];
                if (r == null || r.Sequence <= _lastSeenResult) continue;
                _lastSeenResult = r.Sequence;
                if (string.IsNullOrEmpty(r.Message)) continue;
                _status.Text((r.Success ? "Done: " : "Not done: ") + r.Message);
                _status.Color(r.Success ? Theme.Good : Theme.Warn, r.Success ? 2 : 3);
            }
        }

        private void RememberPosition()
        {
            Vector3 p = absolutePosition;
            if (p.x == _lastSavedPosition.x && p.y == _lastSavedPosition.y) { _positionSettle = 0f; return; }
            _positionSettle += Time.deltaTime;
            if (_positionSettle < 0.5f) return; // save once the drag has settled
            _positionSettle = 0f;
            _lastSavedPosition = p;
            UiPrefs.SetFloat("X", p.x);
            UiPrefs.SetFloat("Y", p.y);
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }
    }
}
