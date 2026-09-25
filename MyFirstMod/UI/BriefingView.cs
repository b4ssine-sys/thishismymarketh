using ColossalFramework.UI;
using UnityEngine;

namespace MyFirstMod
{
    // WO-39: the Treasury Briefing. Three cards the first time the window opens,
    // ending with a suggested first action: price an Emergency Note.
    public sealed class BriefingView
    {
        private static readonly string[] Titles =
        {
            "1 of 3  -  Your treasury",
            "2 of 3  -  Borrowing",
            "3 of 3  -  Your first move"
        };

        private static readonly string[] Bodies =
        {
            "The city has a credit rating, from AAA down to D. Three numbers decide it: debt service coverage (DSCR), " +
            "debt burden, and months of cash reserves. The Treasury workspace shows each one, with a tick where the next notch up begins.",

            "You borrow by issuing bonds. On the Borrow ticket you pick the yield you offer. Investors answer with a bid-to-cover: " +
            "above 1.0x the whole issue sells, below 0.75x the auction fails. The ticket shows the outcome before you commit.",

            "Start small. An Emergency Note is the smallest, shortest bond: a cheap way to build a credit history. " +
            "Open the ticket, leave the yield at the clearing price, and press Issue. Payments due show up on the ladder beside it."
        };

        private readonly BondMarketWindow _window;
        private readonly UIPanel _panel;
        private readonly UILabel _title;
        private readonly UILabel _body;
        private readonly UIButton _next;
        private readonly UIButton _skip;
        private int _card;

        public BriefingView(BondMarketWindow window)
        {
            _window = window;
            _panel = window.AddUIComponent<UIPanel>();
            _panel.backgroundSprite = Theme.CardSprite;
            _panel.size = new Vector2(520f, 250f);
            _panel.relativePosition = new Vector3((window.width - 520f) / 2f, 150f);
            _panel.BringToFront();

            _title = Widgets.Label(_panel, 20f, 16f, 480f, 28f, 1.05f, "", null);
            _title.textColor = Theme.Accent;
            _body = Widgets.Label(_panel, 20f, 52f, 480f, 130f, 0.85f, "", null);
            _body.verticalAlignment = UIVerticalAlignment.Top;
            _skip = Widgets.Button(_panel, "Skip", 20f, 200f, 120f, 32f, "Close the briefing.",
                delegate(UIComponent c, UIMouseEventParameter p) { Finish(false); });
            _next = Widgets.Button(_panel, "Next", 300f, 200f, 200f, 32f, null, OnNext);
            ShowCard(0);
        }

        private void ShowCard(int card)
        {
            _card = card;
            _title.text = Titles[card];
            _body.text = Bodies[card];
            _next.text = card == Titles.Length - 1 ? "Open the ticket" : "Next";
        }

        private void OnNext(UIComponent c, UIMouseEventParameter p)
        {
            if (_card < Titles.Length - 1) ShowCard(_card + 1);
            else Finish(true);
        }

        private void Finish(bool openTicket)
        {
            Close();
            _window.BriefingClosed(openTicket);
        }

        public void Close()
        {
            if (_panel != null) UnityEngine.Object.Destroy(_panel.gameObject);
        }
    }
}
