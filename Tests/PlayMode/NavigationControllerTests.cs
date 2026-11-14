using System.Collections;
using NUnit.Framework;
using Threads.UiFrames.Input;
using Threads.UiFrames.Testing;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Threads.UiFrames.Tests
{
    /// <summary>The Input System side of keyboard navigation: keys fed to a navigator, and key repeat.</summary>
    public class NavigationControllerTests : InputTestFixture
    {
        Keyboard _kb;
        ManualClock _clock;
        FrameSet _set;
        FrameNavigator _nav;
        FrameNavigationController _controller;
        StripFrame _strip;

        public override void Setup()
        {
            base.Setup();
            _kb = InputSystem.AddDevice<Keyboard>();
            _clock = new ManualClock();
            var registry = FrameRegistry.WithBuiltIns();
            var env = new FrameEnvironment(_clock);
            _set = new FrameSet();
            var root = new FakeFrameElement("root", 0, 0, 1000, 800);
            var el = root.Add(new FakeFrameElement("strip", 0, 0, 300, 100, FrameTags.CardStrip));
            var content = el.Add(new FakeFrameElement("cards", 0, 0, 1000, 100, FrameTags.Cards));
            for (var i = 0; i < 10; i++) content.Add(new FakeFrameElement("card" + i, i * 100, 0, 100, 100));
            _strip = (StripFrame)registry.Resolve(el).Create(new FrameContext(el, env, registry));
            _set.Add(_strip);
            _nav = new FrameNavigator(_set, new FakeFocusHost(root));
            _nav.Startup();
            _controller = new FrameNavigationController(_set);
            _controller.Navigators.Add(_nav);
            _controller.KeyNavigators.Add(_nav);
            _controller.Startup();
        }

        public override void TearDown()
        {
            _controller.Dispose();
            _nav.Shutdown();
            base.TearDown();
        }

        int CardIndex()
        {
            var cards = _strip.Content.Children;
            for (var i = 0; i < cards.Count; i++) if (cards[i] == _nav.Selected) return i;
            return -1;
        }

        [UnityTest]
        public IEnumerator KeysReachTheNavigator()
        {
            PressAndRelease(_kb.tabKey);
            yield return null;
            Assert.AreSame(_strip.Element, _nav.Selected);
            PressAndRelease(_kb.enterKey);
            yield return null;
            Assert.AreEqual(0, CardIndex());
            Press(_kb.rightArrowKey);
            yield return null;
            Release(_kb.rightArrowKey);
            yield return null;
            Assert.AreEqual(1, CardIndex());
            PressAndRelease(_kb.escapeKey);
            yield return null;
            Assert.AreSame(_strip.Element, _nav.Selected);
        }

        [UnityTest]
        public IEnumerator EachGamepadUsesItsOwnConfirmAndBackButtons()
        {
            // Xbox and PlayStation confirm with the bottom button; Nintendo confirms with A, on the right.
            var pad = InputSystem.AddDevice<Gamepad>();
            PressAndRelease(_kb.tabKey);
            yield return null;
            PressAndRelease(pad.buttonSouth);
            yield return null;
            Assert.AreEqual(0, CardIndex(), "A / Cross confirms");
            PressAndRelease(pad.buttonEast);
            yield return null;
            Assert.AreSame(_strip.Element, _nav.Selected, "B / Circle goes back");

#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_SWITCH
            var nintendo = InputSystem.AddDevice<UnityEngine.InputSystem.Switch.SwitchProControllerHID>();
            PressAndRelease(nintendo.buttonSouth); // labelled B: back, not confirm
            yield return null;
            Assert.AreSame(_strip.Element, _nav.Selected, "Nintendo's B does not confirm");
            PressAndRelease(nintendo.buttonEast); // labelled A
            yield return null;
            Assert.AreEqual(0, CardIndex(), "Nintendo's A confirms");
            PressAndRelease(nintendo.buttonSouth);
            yield return null;
            Assert.AreSame(_strip.Element, _nav.Selected, "Nintendo's B goes back");
#endif
        }

        [UnityTest]
        public IEnumerator HoldingAnArrowRepeats()
        {
            PressAndRelease(_kb.tabKey);
            PressAndRelease(_kb.enterKey);
            yield return null;
            Press(_kb.rightArrowKey);
            yield return null;
            Assert.AreEqual(1, CardIndex(), "the press steps once");
            for (var t = 0f; t < 0.45f; t += 0.05f) { _clock.Advance(0.05f); _controller.Update(_clock.Now); }
            Assert.AreEqual(1, CardIndex(), "no repeat before the delay");
            for (var t = 0f; t < 0.35f; t += 0.05f) { _clock.Advance(0.05f); _controller.Update(_clock.Now); }
            Assert.AreEqual(4, CardIndex(), "then one step per 0.1 s");
            Release(_kb.rightArrowKey);
            yield return null;
            for (var t = 0f; t < 0.5f; t += 0.05f) { _clock.Advance(0.05f); _controller.Update(_clock.Now); }
            Assert.AreEqual(4, CardIndex(), "stops on release");
        }
    }
}
