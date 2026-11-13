using System.Collections.Generic;
using NUnit.Framework;
using Threads.UiFrames.Testing;

namespace Threads.UiFrames.Tests
{
    /// <summary>
    /// Registration and precedence, and an end-to-end "other package" kind: its own marker class,
    /// property and command, tested against the fake element as a third party would.
    /// </summary>
    public class RegistryTests
    {
        // ---- A kind as another package would write it ----

        static readonly FrameProperty<float> Tilt = new FrameProperty<float>("--spin-tilt",
            (string v, out float r, out string e) =>
            {
                e = "must be a number of degrees.";
                return FrameStyleParser.TryParseNumber(v, out r);
            });

        static readonly FrameCommandId Spin = new FrameCommandId("spin.spin");

        sealed class SpinKind : IFrameKind
        {
            public readonly string Marker;
            public SpinKind(string marker = "spin-wheel") { Marker = marker; }
            public string MarkerClass => Marker;
            public IReadOnlyList<FrameProperty> Properties => new FrameProperty[] { Tilt, FrameProperties.Duration };
            public IFrameBehaviour Create(FrameContext context) => new SpinFrame(context.Element);
        }

        sealed class SpinFrame : IFrameBehaviour
        {
            public SpinFrame(IFrameElement element) { Element = element; }
            public IFrameElement Element { get; }
            public FrameStyle Style { get; private set; } = new FrameStyle();
            public FrameAxis DragAxis => FrameAxis.Horizontal;
            public float Turns;
            public float ToCommandDelta(float dx, float dy) => dx;
            public void ApplyStyle(FrameStyle style) => Style = style.Clone();
            public void OnLayoutChanged() { }
            public void Update() { }
            public void Dispose() { }

            public bool Execute(in FrameCommand command)
            {
                if (command.Id != Spin) return false;
                Turns += command.DeltaDp;
                Element.SetClass("spin-spinning", true);
                return true;
            }
        }

        // ----

        FrameRegistry _r;

        [SetUp]
        public void SetUp() => _r = FrameRegistry.WithBuiltIns();

        [Test]
        public void BuiltInsAreRegisteredLikeAnyKind()
        {
            CollectionAssert.AreEqual(new[] { "uf-drawer", "uf-card-strip", "uf-card-deck" }, KindNames());
            Assert.IsNotNull(_r.FindProperty("--uf-edge"));
            Assert.IsNotNull(_r.FindProperty("--uf-input"));
        }

        [Test]
        public void EmptyRegistryHasCommonProperties()
        {
            var r = new FrameRegistry();
            CollectionAssert.IsEmpty(r.Kinds);
            Assert.IsNotNull(r.FindProperty("--uf-duration"));
            Assert.IsNull(r.FindProperty("--uf-edge"));
        }

        [Test]
        public void MostRecentRegistrationWinsOnAnElement()
        {
            var both = new FakeFrameElement("both", 0, 0, 100, 100, "uf-drawer", "spin-wheel");
            Assert.IsInstanceOf<DrawerKind>(_r.Resolve(both));
            _r.Add(new SpinKind());
            Assert.IsInstanceOf<SpinKind>(_r.Resolve(both));
        }

        [Test]
        public void ReregisteringMovesAKindToTheTop()
        {
            var el = new FakeFrameElement("e", 0, 0, 100, 100, "uf-drawer", "spin-wheel");
            _r.Add(new SpinKind());
            _r.Add(new DrawerKind());
            Assert.IsInstanceOf<DrawerKind>(_r.Resolve(el));
            Assert.AreEqual(1, CountOf("uf-drawer"), "the old registration is replaced, not duplicated");
        }

        [Test]
        public void SameMarkerReplacesTheBuiltIn()
        {
            _r.Add(new SpinKind("uf-drawer"));
            var el = new FakeFrameElement("e", 0, 0, 100, 100, "uf-drawer");
            Assert.IsInstanceOf<SpinKind>(_r.Resolve(el));
            Assert.AreEqual(1, CountOf("uf-drawer"));
        }

        [Test]
        public void RemoveFallsBackToTheNextKind()
        {
            var el = new FakeFrameElement("e", 0, 0, 100, 100, "uf-drawer", "spin-wheel");
            _r.Add(new SpinKind());
            Assert.IsTrue(_r.Remove("spin-wheel"));
            Assert.IsInstanceOf<DrawerKind>(_r.Resolve(el));
        }

        [Test]
        public void UntaggedElementHasNoKind() =>
            Assert.IsNull(_r.Resolve(new FakeFrameElement("plain", 0, 0, 10, 10, "button")));

        [Test]
        public void PropertyTypeClashFailsWithoutChangingAnything()
        {
            var clash = new FrameProperty<string>("--uf-edge", (string v, out string r, out string e) => { r = v; e = null; return true; });
            var kind = new ClashKind(clash);
            Assert.Throws<System.InvalidOperationException>(() => _r.Add(kind));
            Assert.IsNull(_r.FindKind("clash"));
            Assert.AreSame(FrameProperties.Edge, _r.FindProperty("--uf-edge"));
        }

        [Test]
        public void SameTypePropertyIsReplacedMostRecentWins()
        {
            var lenient = FrameStyleParser.Keyword("--uf-edge", ("west", FrameEdge.Left));
            _r.AddProperty(lenient);
            Assert.AreSame(lenient, _r.FindProperty("--uf-edge"));
            var s = new FrameStyle();
            Assert.IsTrue(_r.TryApply(s, "--uf-edge", "west", out _));
            Assert.AreEqual(1, Count(_r.Properties, "--uf-edge"));
        }

        [Test]
        public void ChangedIsRaised()
        {
            var n = 0;
            _r.Changed += () => n++;
            _r.Add(new SpinKind());
            _r.Remove("spin-wheel");
            Assert.AreEqual(2, n);
        }

        [Test]
        public void ThirdPartyKindEndToEnd()
        {
            _r.Add(new SpinKind());
            Assert.IsNotNull(_r.FindProperty("--spin-tilt"), "a kind's properties are registered with it");

            var el = new FakeFrameElement("wheel", 0, 0, 200, 200, "spin-wheel");
            var kind = _r.Resolve(el);
            var frame = (SpinFrame)kind.Create(new FrameContext(el, new FrameEnvironment(new ManualClock()), _r));

            // What a binder does with the raw values it read from USS or the uGUI stylesheet:
            var style = new FrameStyle();
            Assert.IsTrue(_r.TryApply(style, "--spin-tilt", "12.5", out var error), error);
            Assert.IsFalse(_r.TryApply(style, "--spin-tilt", "lots", out error));
            StringAssert.Contains("--spin-tilt", error);
            frame.ApplyStyle(style);
            Assert.AreEqual(12.5f, frame.Style.Get(Tilt, 0f));

            Assert.IsTrue(frame.Execute(new FrameCommand(Spin, deltaDp: 3f)));
            Assert.IsFalse(frame.Execute(FrameCommand.Open), "unknown ids are passed on");
            Assert.AreEqual(3f, frame.Turns);
            Assert.IsTrue(el.HasClass("spin-spinning"));
        }

        sealed class ClashKind : IFrameKind
        {
            readonly FrameProperty _p;
            public ClashKind(FrameProperty p) { _p = p; }
            public string MarkerClass => "clash";
            public IReadOnlyList<FrameProperty> Properties => new[] { _p };
            public IFrameBehaviour Create(FrameContext context) => null;
        }

        List<string> KindNames()
        {
            var l = new List<string>();
            foreach (var k in _r.Kinds) l.Add(k.MarkerClass);
            return l;
        }

        int CountOf(string marker)
        {
            var n = 0;
            foreach (var k in _r.Kinds) if (k.MarkerClass == marker) n++;
            return n;
        }

        static int Count(IReadOnlyList<FrameProperty> list, string name)
        {
            var n = 0;
            foreach (var p in list) if (p.Name == name) n++;
            return n;
        }
    }
}
