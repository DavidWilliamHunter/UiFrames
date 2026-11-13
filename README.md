# UI Frames

Interactive UI frames for Unity 6: **drawers**, **card strips** and **card decks**, for both **UI Toolkit** (UXML/USS) and **uGUI** (Canvas and GameObjects).

- Designers say *what* a frame does with tags: style classes such as `uf-drawer` and custom properties such as `--uf-edge: left`. In UI Toolkit these are real USS classes and properties; in uGUI a `UiFrameClasses` component and a `UiFrameStyleSheet` asset use the same vocabulary.
- Controllers decide *how* each device drives a frame: touch (through `com.threads.touchcontroller`, when installed), mouse and pen, keyboard and gamepad. They all send the same semantic commands (Open, Close, Next, ScrollBy, Drag…), so a frame behaves the same whatever the input.
- The motion maths is plain C# and unit tested.

Status: early preview. See `Documentation~/plan.md`.

## Adding your own frames

Another package can add tags without changing this one:

```csharp
public static readonly FrameProperty<float> Tilt = new FrameProperty<float>("--spin-tilt",
    (string v, out float r, out string e) => { e = "must be a number"; return FrameStyleParser.TryParseNumber(v, out r); });

sealed class SpinKind : IFrameKind
{
    public string MarkerClass => "spin-wheel";
    public IReadOnlyList<FrameProperty> Properties => new FrameProperty[] { Tilt, FrameProperties.Duration };
    public IFrameBehaviour Create(FrameContext context) => new SpinFrame(context);
}

var registry = FrameRegistry.WithBuiltIns().Add(new SpinKind());
```

The most recent registration wins: if an element has both `uf-drawer` and `spin-wheel`, it gets whichever kind was registered last, and registering a kind for an existing marker class replaces it. Behaviours only see `IFrameElement`, so the same class runs in UI Toolkit and uGUI. Test them with `FakeFrameElement` and `ManualClock` from `Threads.UiFrames.Testing`.
