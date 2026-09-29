using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace PoRumble.Tests
{
    /// <summary>
    /// The portrait HUD fits one screen: every panel between the two chrome rows, inside the
    /// width, and clear of every other panel on screen at the same time.
    ///
    /// Each HUD panel is its own UIDocument, absolutely positioned against its own root, so
    /// nothing in UI Toolkit stops two of them claiming the same rectangle or one running off
    /// the bottom of a phone - and every time that has happened here it happened silently. The
    /// fight card ran under both chrome bars with a full card; the result banner drew through
    /// the standings on every results screen; the second-fight feed sat on the version label.
    /// Each was found by looking at a device.
    ///
    /// These lay the real UXML and the real stylesheet out in an editor panel at phone sizes,
    /// filled to the worst case the views produce - ten fighters, long names, every line of
    /// text populated - and measure. What they do not do is run the views: the classes each view
    /// adds for a phase are applied here by hand, so a view that starts adding a new class will
    /// need its scenario updated to match.
    /// </summary>
    public sealed class HudLayoutTests
    {
        private const string LAYOUTS = "Assets/UI/Layouts/";
        private const string TEMPLATES = "Assets/UI/Layouts/Templates/";
        private const string STYLESHEET = "Assets/UI/Styles/porumble.uss";

        /// <summary>The panel reference width. PanelSettings match width, so every phone is 1080 wide in panel units.</summary>
        private const float WIDTH = 1080f;

        /// <summary>SafeAreaView's inset on every edge before any notch is added.</summary>
        private const float SAFE_INSET = 12f;

        /// <summary>
        /// A status bar with a punch-hole camera, in panel units at 1080 wide. Each layout is run
        /// with and without it: SafeAreaView once wrote the inset as root padding, which absolute
        /// panels ignore, and a probe that never had a notch could not tell.
        /// </summary>
        private const float NOTCH = 120f;

        /// <summary>Every seat in the ring: twenty boxers, one health row each.</summary>
        private const int FULL_FIELD = 20;

        /// <summary>Every contestant on the title screen: the seventeen named fighters, the heuristic and the reference policy.</summary>
        private const int FULL_CARD = 19;

        /// <summary>Half a unit of slack for sub-pixel layout rounding.</summary>
        private const float SLACK = 0.5f;

        private static readonly string[] FighterNames =
        {
            "STANDARDRL", "HEURISTIC", "FIGHTER 03", "FIGHTER 04", "FIGHTER 05",
            "FIGHTER 06", "FIGHTER 07", "FIGHTER 08", "CHECKPOINT 09", "CHECKPOINT 10",
            "MAGA MIKE", "HEGSETH", "FIGHTER 13", "FIGHTER 14", "FIGHTER 15",
            "FIGHTER 16", "FIGHTER 17", "FIGHTER 18", "STANDARD RL 2", "HEURISTIC 2"
        };

        private static readonly string[] StatNames = { "THROWN", "LANDED", "CONNECT", "BLOCKED", "SLIPS", "DAMAGE" };

        private ProbeScreen _screen;

        [TearDown]
        public void TearDown()
        {
            _screen?.Dispose();
            _screen = null;
        }

        [UnityTest]
        public IEnumerator TitleScreen_WithAFullCard_FitsBetweenTheChromeRows(
            [Values(1920f, 2340f, 2400f, 2520f)] float height,
            [Values(0f, NOTCH)] float notch)
        {
            _screen = new ProbeScreen(WIDTH, height, notch);
            Chrome chrome = AddChrome(_screen);

            VisualElement menu = _screen.AddDocument("MainMenu.uxml");
            VisualElement grid = menu.Q<VisualElement>("grid");
            VisualTreeAsset tile = LoadTemplate("RosterTile.uxml");

            for (int index = 0; index < FULL_CARD; index++)
            {
                tile.CloneTree(grid);
                VisualElement clone = grid[grid.childCount - 1];
                clone.Q<Label>("name").text = FighterNames[index];
                clone.Q<Label>("generation").text = index >= 8 ? "12.5M STEPS" : string.Empty;
                clone.Q<Label>("price").text = "WINS 1250";
                clone.Q<Label>("standing").text = "1234";

                // Nineteen entrants in twenty corners: the first takes a second one.
                Label seats = clone.Q<Label>("seats");
                seats.text = "x2";
                seats.EnableInClassList("roster-tile__seats--gone", index >= 1);
            }

            menu.Q<Label>("bank").text = "BANK 12400   RIGHT 12 OF 30";
            menu.Q<Label>("hint").text =
                "YOUR 100 IS ON CHECKPOINT 10   WINS 1250 IF THEY WIN\nENTER TO FIGHT      TAB SWITCHES PICK / CARD";

            yield return _screen.Settle();

            AssertBetweenChromeRows(chrome, menu.Q<VisualElement>("panel"), "the title screen");
            AssertInThumbReach(chrome, menu.Q<VisualElement>("fight"), "FIGHT");
        }

        [UnityTest]
        public IEnumerator LiveFight_StripFeedAndPlayerPanel_FitAndNeverOverlap(
            [Values(1920f, 2340f, 2400f, 2520f)] float height,
            [Values(0f, NOTCH)] float notch)
        {
            _screen = new ProbeScreen(WIDTH, height, notch);
            Chrome chrome = AddChrome(_screen);

            VisualElement strip = AddFieldBoard(_screen, compact: true);
            Label caption = strip.parent.Q<Label>("caption");
            caption.text = "FIGHT!";

            Label commentary = AddCommentary(_screen);

            VisualElement feed = _screen.AddDocument("PictureInPicture.uxml").Q<VisualElement>("panel");
            feed.RemoveFromClassList("pip--hidden");
            feed.Q<VisualElement>("feed").style.height = 272f;
            feed.Q<Label>("caption").text = "ALSO IN THE RING   CHECKPOINT 09  VS  CHECKPOINT 10";

            VisualElement player = AddPlayerPanel(_screen);

            VisualElement debug = AddDiagnostics(_screen, expanded: false);

            VisualElement knockouts = AddKnockoutFeed(_screen);

            yield return _screen.Settle();

            AssertBetweenChromeRows(chrome, strip, "the fight strip");
            AssertRowsReadable(strip, "the fight strip");
            AssertBetweenChromeRows(chrome, feed, "the second-fight feed");
            AssertBetweenChromeRows(chrome, player, "the player's panel");
            AssertBetweenChromeRows(chrome, debug, "the folded debug sheet");
            AssertBetweenChromeRows(chrome, knockouts, "the knockout feed");

            AssertApart(knockouts, "the knockout feed", strip, "the fight strip");
            AssertApart(knockouts, "the knockout feed", feed, "the second-fight feed");
            AssertApart(knockouts, "the knockout feed", player, "the player's panel");

            AssertApart(strip, "the fight strip", feed, "the second-fight feed");
            AssertApart(strip, "the fight strip", player, "the player's panel");
            AssertApart(feed, "the second-fight feed", player, "the player's panel");
            AssertApart(debug, "the folded debug sheet", feed, "the second-fight feed");
            AssertApart(debug, "the folded debug sheet", player, "the player's panel");
            AssertApart(debug, "the folded debug sheet", strip, "the fight strip");

            AssertCommentaryInItsRow(chrome, commentary);
        }

        [UnityTest]
        public IEnumerator ResultsScreen_BoardSlotAndCard_FitAndNeverOverlap(
            [Values(1920f, 2340f, 2400f, 2520f)] float height,
            [Values(0f, NOTCH)] float notch)
        {
            _screen = new ProbeScreen(WIDTH, height, notch);
            Chrome chrome = AddChrome(_screen);

            VisualElement board = AddFieldBoard(_screen, compact: false);
            VisualElement hud = board.parent;
            board.Q<Label>("pick").AddToClassList("match-hud__pick--gone");

            VisualElement results = hud.Q<VisualElement>("results");
            results.RemoveFromClassList("results--hidden");
            hud.Q<Label>("result").text = "CHECKPOINT 10 WINS";
            hud.Q<Label>("result-rating").text = "1532 ELO  +14";
            hud.Q<Label>("result-pick").text = "YOUR 100 ON CHECKPOINT 10 WON 1250   BANK 12400";
            hud.Q<Label>("prompt").text = "ENTER  REMATCH      R  MENU";

            // The tallest the card gets: a winner with a face and a stats line.
            hud.Q<VisualElement>("result-portrait").RemoveFromClassList("results__portrait--gone");
            Label resultStats = hud.Q<Label>("result-stats");
            resultStats.RemoveFromClassList("results__stats--gone");
            resultStats.text = "4 KNOCKOUTS   128 DAMAGE   41% LANDED";

            VisualElement stats = AddTelemetry(_screen);
            stats.AddToClassList("hud-carousel--slotted");

            VisualElement standings = AddStandings(_screen);
            standings.AddToClassList("hud-carousel--slotted");
            standings.AddToClassList("hud-carousel--off");

            VisualElement tabs = _screen.AddDocument("HudCarouselTabs.uxml").Q<VisualElement>("tabs");
            tabs.RemoveFromClassList("carousel-tabs--hidden");
            AddTab(tabs, "TAPE", on: true);
            AddTab(tabs, "TABLE", on: false);

            Label commentary = AddCommentary(_screen);

            yield return _screen.Settle();

            AssertBetweenChromeRows(chrome, board, "the field board");
            AssertRowsReadable(board, "the field board");
            AssertBetweenChromeRows(chrome, tabs, "the carousel tabs");
            AssertBetweenChromeRows(chrome, stats, "the telemetry board");
            AssertBetweenChromeRows(chrome, standings, "the standings");
            AssertBetweenChromeRows(chrome, results, "the results card");

            AssertApart(board, "the field board", tabs, "the carousel tabs");
            AssertApart(board, "the field board", stats, "the telemetry board");
            AssertApart(board, "the field board", standings, "the standings");
            AssertApart(board, "the field board", results, "the results card");
            AssertApart(tabs, "the carousel tabs", stats, "the telemetry board");
            AssertApart(stats, "the telemetry board", results, "the results card");
            AssertApart(standings, "the standings", results, "the results card");
            AssertInThumbReach(chrome, results, "the results card");

            AssertCommentaryInItsRow(chrome, commentary);
        }

        [UnityTest]
        public IEnumerator Countdown_TaleOfTheTape_FitsUnderTheBoardAndClearOfTheNumber(
            [Values(1920f, 2340f, 2400f, 2520f)] float height,
            [Values(0f, NOTCH)] float notch)
        {
            _screen = new ProbeScreen(WIDTH, height, notch);
            Chrome chrome = AddChrome(_screen);

            VisualElement board = AddFieldBoard(_screen, compact: false);
            Label caption = board.parent.Q<Label>("caption");
            caption.text = "3";

            VisualElement standings = AddStandings(_screen);
            standings.AddToClassList("hud-carousel--slotted");

            VisualElement tape = AddTaleOfTheTape(_screen);

            yield return _screen.Settle();

            AssertBetweenChromeRows(chrome, tape, "the tale of the tape");
            AssertRowsReadable(board, "the field board");
            AssertApart(tape, "the tale of the tape", board, "the field board");
            AssertApart(tape, "the tale of the tape", standings, "the standings");
            AssertApart(tape, "the tale of the tape", caption, "the countdown");
        }

        [UnityTest]
        public IEnumerator DebugSheet_OpenedOut_FitsBetweenTheChromeRows(
            [Values(1920f, 2340f, 2400f, 2520f)] float height,
            [Values(0f, NOTCH)] float notch)
        {
            _screen = new ProbeScreen(WIDTH, height, notch);
            Chrome chrome = AddChrome(_screen);

            VisualElement debug = AddDiagnostics(_screen, expanded: true);

            yield return _screen.Settle();

            AssertBetweenChromeRows(chrome, debug, "the opened debug sheet");
        }

        // ------------------------------------------------------------------ scenario builders

        private readonly struct Chrome
        {
            public readonly VisualElement Top;
            public readonly VisualElement Bottom;
            public readonly VisualElement Debug;
            public readonly VisualElement Version;

            public Chrome(VisualElement top, VisualElement bottom, VisualElement debug, VisualElement version)
            {
                Top = top;
                Bottom = bottom;
                Debug = debug;
                Version = version;
            }
        }

        private static Chrome AddChrome(ProbeScreen screen)
        {
            VisualElement root = screen.AddDocument("AppChrome.uxml");
            root.Q<Label>("status").text = "12:42   ROPES CLOSING";

            Label version = root.Q<Label>("version");
            version.text = "v1.5.0 (123)";

            return new Chrome(
                root.Q<VisualElement>("chrome-top"),
                root.Q<VisualElement>("chrome-bottom"),
                root.Q<VisualElement>("debug"),
                version);
        }

        private static VisualElement AddFieldBoard(ProbeScreen screen, bool compact)
        {
            VisualElement root = screen.AddDocument("MatchHud.uxml");
            VisualElement panel = root.Q<VisualElement>("panel");
            VisualElement roster = root.Q<VisualElement>("roster");
            VisualTreeAsset row = LoadTemplate("HealthRow.uxml");

            for (int index = 0; index < FULL_FIELD; index++)
            {
                row.CloneTree(roster);
                VisualElement clone = roster[roster.childCount - 1];
                clone.Q<Label>("name").text = FighterNames[index];
                clone.Q<Label>("odds").text = "<1%";
            }

            root.Q<Label>("survivors").text = "20 / 20 LEFT";
            root.Q<Label>("bout").text = "BOUT 12";
            root.Q<Label>("pick").text = "YOUR PICK  CHECKPOINT 10   PAYS 11.5x   NOW 14%";

            panel.EnableInClassList("match-hud--compact", compact);
            return panel;
        }

        private static VisualElement AddTelemetry(ProbeScreen screen)
        {
            VisualElement root = screen.AddDocument("FightStats.uxml");
            VisualElement rows = root.Q<VisualElement>("rows");
            VisualTreeAsset row = LoadTemplate("StatRow.uxml");

            root.Q<Label>("name-a").text = FighterNames[8];
            root.Q<Label>("name-b").text = FighterNames[9];
            root.Q<Label>("share-a").text = "62%";
            root.Q<Label>("share-b").text = "38%";

            for (int index = 0; index < StatNames.Length; index++)
            {
                row.CloneTree(rows);
                VisualElement clone = rows[rows.childCount - 1];
                clone.Q<Label>("label").text = StatNames[index];
                clone.Q<Label>("value-a").text = "100%";
                clone.Q<Label>("value-b").text = "100%";
            }

            return root.Q<VisualElement>("panel");
        }

        private static VisualElement AddStandings(ProbeScreen screen)
        {
            VisualElement root = screen.AddDocument("Standings.uxml");
            VisualElement rows = root.Q<VisualElement>("rows");
            VisualTreeAsset row = LoadTemplate("StandingsRow.uxml");

            for (int place = 0; place < 3; place++)
            {
                row.CloneTree(rows);
                ((Label)rows[rows.childCount - 1]).text = "1  CHECKPOINT 10   1532  +14";
            }

            return root.Q<VisualElement>("panel");
        }

        /// <summary>All three knockout lines up at once, each as long as a line gets.</summary>
        private static VisualElement AddKnockoutFeed(ProbeScreen screen)
        {
            VisualElement root = screen.AddDocument("EliminationFeed.uxml");
            VisualElement panel = root.Q<VisualElement>("panel");

            for (int index = 0; index < 3; index++)
            {
                Label toast = root.Q<Label>("toast-" + index);
                toast.text = "CHECKPOINT 09 2 KO'D BY CHECKPOINT 10 2    FINAL TWO";
                toast.AddToClassList("feed__toast--on");
            }

            return panel;
        }

        /// <summary>The tape with both faces and every line filled, the tallest it gets.</summary>
        private static VisualElement AddTaleOfTheTape(ProbeScreen screen)
        {
            VisualElement root = screen.AddDocument("FightIntro.uxml");
            VisualElement panel = root.Q<VisualElement>("panel");
            panel.RemoveFromClassList("tape--hidden");

            root.Q<Label>("headline").text = "YOUR PICK  VS  THE FAVOURITE";

            foreach (string side in new[] { "a", "b" })
            {
                root.Q<Label>("name-" + side).text = "CHECKPOINT 10";
                root.Q<Label>("rating-" + side).text = "1532 ELO";
                root.Q<Label>("record-" + side).text = "12 WINS IN 30   9 KO";
                root.Q<Label>("price-" + side).text = "28% TO WIN   100 WINS 1250";
            }

            return panel;
        }

        private static Label AddCommentary(ProbeScreen screen)
        {
            VisualElement root = screen.AddDocument("Commentary.uxml");
            Label line = root.Q<Label>("commentary");
            line.text = "CHECKPOINT 10 is finished! He's out of it, and the ring is down to two!";
            line.AddToClassList("commentary--on");
            return line;
        }

        private static VisualElement AddPlayerPanel(ProbeScreen screen)
        {
            VisualElement root = screen.AddDocument("PlayerStatusHud.uxml");
            VisualElement bars = root.Q<VisualElement>("bars");
            VisualTreeAsset bar = LoadTemplate("StatBar.uxml");

            string[] captions = { "HEALTH", "BREATH", "POWER" };

            for (int index = 0; index < captions.Length; index++)
            {
                bar.CloneTree(bars);
                VisualElement clone = bars[bars.childCount - 1];
                clone.Q<Label>("caption").text = captions[index];
                clone.Q<VisualElement>("track").AddToClassList(
                    index == 0 ? "player-hud__bar--health" : "player-hud__bar--minor");
            }

            root.Q<Label>("title").text = "YOU  #00";
            root.Q<Label>("counter").text = "COUNTER READY";
            root.Q<Label>("threat").text = "-> E  #03 BEHIND YOU";

            return root.Q<VisualElement>("panel");
        }

        private static VisualElement AddDiagnostics(ProbeScreen screen, bool expanded)
        {
            VisualElement root = screen.AddDocument("Diagnostics.uxml");
            VisualElement panel = root.Q<VisualElement>("panel");

            panel.EnableInClassList("diag--collapsed", !expanded);

            root.Q<Label>("verdict").text = expanded
                ? "Frame time over budget: 21.4ms against 16.7. Look at the renderer first.\n" +
                  "Allocating 48 KB/s in the update loops. Open the profiler's GC Alloc column.\n" +
                  "Policy stepping at 3.1/s with 10 agents. Inference is not keeping up."
                : "Frame time over budget: 21.4ms against 16.7. Look at the renderer first.   (+2 more)";

            var readout = new System.Text.StringBuilder();

            for (int line = 0; line < 9; line++)
            {
                readout.Append("draw     128   setpass 64   tris 123456   rendertex 12\n");
            }

            root.Q<Label>("readout").text = readout.ToString();
            return panel;
        }

        private static void AddTab(VisualElement tabs, string label, bool on)
        {
            var tab = new Button { text = label };
            tab.AddToClassList("text");
            tab.AddToClassList("text--xs");
            tab.AddToClassList("text--bold");
            tab.AddToClassList("carousel-tab");
            tab.EnableInClassList("carousel-tab--on", on);
            tabs.Add(tab);
        }

        private static VisualTreeAsset LoadTemplate(string file)
        {
            var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TEMPLATES + file);
            Assert.That(asset, Is.Not.Null, $"{TEMPLATES}{file} did not load");
            return asset;
        }

        // ------------------------------------------------------------------ assertions

        private void AssertBetweenChromeRows(Chrome chrome, VisualElement element, string what)
        {
            Rect rect = _screen.RectOf(element);
            float top = _screen.RectOf(chrome.Top).yMax;
            float bottom = _screen.RectOf(chrome.Bottom).yMin;

            // The chrome carries MENU, the only way out of a live fight on a phone; under the
            // status bar it cannot be tapped.
            Assert.That(_screen.RectOf(chrome.Top).yMin, Is.GreaterThanOrEqualTo(_screen.SafeTop - SLACK),
                $"the top chrome row starts inside the notch, which ends at {_screen.SafeTop}");
            Assert.That(_screen.RectOf(chrome.Bottom).yMax, Is.LessThanOrEqualTo(_screen.SafeBottom + SLACK),
                $"the bottom chrome row runs past the safe area, which ends at {_screen.SafeBottom}");

            Assert.That(rect.width, Is.GreaterThan(0f), $"{what} has no width - did it lay out?");
            Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(-SLACK), $"{what} {rect} runs off the left edge");
            Assert.That(rect.xMax, Is.LessThanOrEqualTo(_screen.Width + SLACK), $"{what} {rect} runs off the right edge");
            Assert.That(rect.yMin, Is.GreaterThanOrEqualTo(top - SLACK),
                $"{what} {rect} starts inside the top chrome row, which ends at {top}");
            Assert.That(rect.yMax, Is.LessThanOrEqualTo(bottom + SLACK),
                $"{what} {rect} runs into the bottom chrome row, which starts at {bottom}");
        }

        private void AssertApart(VisualElement a, string aName, VisualElement b, string bName)
        {
            Rect rectA = _screen.RectOf(a);
            Rect rectB = _screen.RectOf(b);

            bool overlap = rectA.xMin < rectB.xMax - SLACK && rectB.xMin < rectA.xMax - SLACK &&
                           rectA.yMin < rectB.yMax - SLACK && rectB.yMin < rectA.yMax - SLACK;

            Assert.That(overlap, Is.False, $"{aName} {rectA} overlaps {bName} {rectB}");
        }

        /// <summary>
        /// Every fighter's row lies inside the board and its name is drawn at full height. The
        /// board's height cap held the panel on screen with twenty rows while the rows inside it
        /// were squeezed until every name was cut in half, and the panel-level checks all passed.
        /// </summary>
        private void AssertRowsReadable(VisualElement board, string what)
        {
            Rect panel = _screen.RectOf(board);
            VisualElement roster = board.Q<VisualElement>("roster");

            Assert.That(roster.childCount, Is.EqualTo(FULL_FIELD), $"{what} should carry every seat");

            for (int index = 0; index < roster.childCount; index++)
            {
                Rect row = _screen.RectOf(roster[index]);
                Label name = roster[index].Q<Label>("name");
                float lineHeight = name.resolvedStyle.fontSize;

                Assert.That(row.yMax, Is.LessThanOrEqualTo(panel.yMax + SLACK),
                    $"{what}: row {index} {row} runs out of the panel {panel}");
                Assert.That(_screen.RectOf(name).height, Is.GreaterThanOrEqualTo(lineHeight - SLACK),
                    $"{what}: row {index}'s name is {_screen.RectOf(name).height} tall for a {lineHeight} font");
            }
        }

        /// <summary>
        /// The primary controls sit on the bottom band, where a thumb holding the phone reaches.
        /// They used to sit mid-screen over dim backdrop with the bottom third of the phone empty.
        /// Ending within a quarter of the screen's width of the bottom chrome row counts.
        /// </summary>
        private void AssertInThumbReach(Chrome chrome, VisualElement element, string what)
        {
            Rect rect = _screen.RectOf(element);
            float floor = _screen.RectOf(chrome.Bottom).yMin;

            Assert.That(rect.yMax, Is.GreaterThanOrEqualTo(floor - _screen.Width * 0.25f),
                $"{what} {rect} ends {floor - rect.yMax}px above the bottom chrome row; it should sit on the bottom band");
        }

        /// <summary>
        /// The caption shares the bottom chrome row with DEBUG and the version, so it has to sit
        /// inside that row and clear of both.
        /// </summary>
        private void AssertCommentaryInItsRow(Chrome chrome, Label commentary)
        {
            Rect line = _screen.RectOf(commentary);
            Rect row = _screen.RectOf(chrome.Bottom);

            Assert.That(line.yMin, Is.GreaterThanOrEqualTo(row.yMin - SLACK), $"the commentary {line} rises out of its row {row}");
            Assert.That(line.yMax, Is.LessThanOrEqualTo(row.yMax + SLACK), $"the commentary {line} drops out of its row {row}");

            AssertApart(commentary, "the commentary", chrome.Debug, "DEBUG");
            AssertApart(commentary, "the commentary", chrome.Version, "the version label");
        }

        // ------------------------------------------------------------------ the probe

        /// <summary>
        /// A phone-sized box in an editor window's panel, with one child per HUD document, each
        /// set up the way UIDocument and SafeAreaView set up a document root at runtime: filling
        /// the screen, carrying the stylesheet, inset by the safe area, marked portrait.
        /// </summary>
        private sealed class ProbeScreen : IDisposable
        {
            private readonly HudLayoutProbeWindow _window;
            private readonly StyleSheet _styleSheet;
            private readonly VisualElement _box;
            private readonly float _notch;

            public ProbeScreen(float width, float height, float notch)
            {
                Width = width;
                Height = height;
                _notch = notch;

                _styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(STYLESHEET);
                Assert.That(_styleSheet, Is.Not.Null, $"{STYLESHEET} did not load");

                _window = ScriptableObject.CreateInstance<HudLayoutProbeWindow>();
                _window.titleContent = new GUIContent("HUD layout probe");
                _window.position = new Rect(40f, 40f, 480f, 360f);
                _window.Show();

                // The box is fixed at the phone's size whatever the window is: layout runs on
                // the whole tree, clipped or not, which is what lets a small window measure a
                // 1080x2400 screen.
                _box = new VisualElement();
                _box.style.position = Position.Absolute;
                _box.style.left = 0f;
                _box.style.top = 0f;
                _box.style.width = width;
                _box.style.height = height;
                _window.rootVisualElement.Add(_box);
            }

            public float Width { get; }

            private float Height { get; }

            /// <summary>Where the usable screen starts: below the notch and the base inset.</summary>
            public float SafeTop => _notch + SAFE_INSET;

            /// <summary>Where the usable screen ends.</summary>
            public float SafeBottom => Height - SAFE_INSET;

            public VisualElement AddDocument(string layoutFile)
            {
                var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(LAYOUTS + layoutFile);
                Assert.That(layout, Is.Not.Null, $"{LAYOUTS}{layoutFile} did not load");

                var root = new VisualElement();
                root.style.position = Position.Absolute;
                root.style.left = 0f;
                root.style.top = 0f;
                root.style.right = 0f;
                root.style.bottom = 0f;
                // Margins, as SafeAreaView writes them. Padding would pass every assertion here
                // and inset nothing on a device, because every panel is absolutely positioned.
                root.style.marginLeft = SAFE_INSET;
                root.style.marginRight = SAFE_INSET;
                root.style.marginTop = SafeTop;
                root.style.marginBottom = SAFE_INSET;
                root.AddToClassList("portrait");
                root.styleSheets.Add(_styleSheet);

                layout.CloneTree(root);
                _box.Add(root);
                return root;
            }

            /// <summary>
            /// Lets the editor run the layout pass, and a few more after it: text is measured
            /// once its font atlas is ready, which can be a frame after the first pass.
            /// </summary>
            public IEnumerator Settle()
            {
                int settledFrames = 0;

                for (int frame = 0; frame < 120 && settledFrames < 4; frame++)
                {
                    _window.Repaint();
                    yield return null;

                    bool laidOut = Mathf.Abs(_box.layout.width - Width) < SLACK &&
                                   Mathf.Abs(_box.layout.height - Height) < SLACK;

                    settledFrames = laidOut ? settledFrames + 1 : 0;
                }

                Assert.That(settledFrames, Is.GreaterThanOrEqualTo(4),
                    "the probe panel never laid out; the editor window may not have been shown");
            }

            public Rect RectOf(VisualElement element)
            {
                Rect world = element.worldBound;
                Rect origin = _box.worldBound;
                return new Rect(world.x - origin.x, world.y - origin.y, world.width, world.height);
            }

            public void Dispose()
            {
                if (_window != null)
                {
                    _window.Close();
                }
            }
        }
    }
}
