# UI Frames

Interactive UI frames for Unity 6: **drawers**, **card strips** and **card decks**, for both **UI Toolkit** (UXML/USS) and **uGUI** (Canvas and GameObjects).

- Designers say *what* a frame does with tags: style classes such as `uf-drawer` and custom properties such as `--uf-edge: left`. In UI Toolkit these are real USS classes and properties; in uGUI a `UiFrameClasses` component and a `UiFrameStyleSheet` asset use the same vocabulary.
- Controllers decide *how* each device drives a frame: touch (through `com.threads.touchcontroller`, when installed), mouse and pen, keyboard and gamepad. They all send the same semantic commands (Open, Close, Next, ScrollBy, Drag…), so a frame behaves the same whatever the input.
- The motion maths is plain C# and unit tested.

Status: preview 0.1.0 (mouse, keyboard and gamepad; touch through TouchController is next). Full guide: [Documentation~/Usage.md](Documentation~/Usage.md). Design notes: `Documentation~/plan.md`.

## Quick start

1. Add a **UI Frames Runner** component. List your UIDocuments and/or uGUI Canvases, and for UI Toolkit assign `Runtime/Toolkit/UiFrames.uss` as *Toolkit Defaults*.
2. Tag elements:
   - UI Toolkit: classes in UXML (`class="uf-card-strip"`, with a `uf-cards` child holding the cards) and properties in USS (`--uf-snap: card;`).
   - uGUI: a **UI Frame Classes** component (classes `uf-card-strip`, `uf-cards`…) and a **UI Frames Style Sheet** asset (rules such as `.uf-card-strip` → `--uf-snap: card`), or inline declarations on the component.
3. Press Play. Drag or wheel with the mouse. With a keyboard or gamepad, navigation goes in levels:
   - **Frames:** arrows (d-pad) or Tab move between frames (and controls outside frames). Enter (A) goes in.
   - **Cards:** arrows along the frame's axis or Tab move between cards; the frame scrolls to show the selected card. Enter goes in.
   - **Controls:** arrows or Tab move between the card's controls. Enter presses a button; on a slider or text field it starts adjusting or editing, and Enter or Esc ends it.
   - Esc (B) always goes back one level; leaving a drawer closes it. Levels with one choice are skipped; a card with nothing inside raises `FrameNavigator.Activated`. Page Up/Down, Home/End, F6 (next frame) and gamepad Y (drawer) work at any level.

   The selection carries `uf-selected`, entered frames and cards `uf-entered`, a control being adjusted `uf-editing`. If the mouse scrolls the selected card away, the selection moves to a card in view; a click moves the keyboard selection to what was clicked. A closed drawer's controls cannot be reached. `--uf-navigation: flat` makes a frame transparent to this (its controls join the level around it).

| Class | Children | Properties |
|---|---|---|
| `uf-drawer` (laid out open, against its edge) | optional `uf-drawer-handle` | `--uf-edge: auto\|left\|right\|top\|bottom` |
| `uf-card-strip` | `uf-cards` → cards | `--uf-axis`, `--uf-snap: none\|card`, `--uf-deceleration` |
| `uf-card-deck` | `uf-cards` → cards | `--uf-axis`, `--uf-deck-step: card\|page` |
| any frame | | `--uf-duration` (e.g. `160ms`), `--uf-easing`, `--uf-input: all\|none\|touch pointer navigation`, `--uf-navigation: levels\|flat` |

`uf-no-drag` on an element stops frame drags from starting on it. Frames set `uf-open`, `uf-dragging`, `uf-focused` and `uf-horizontal`/`uf-vertical` so each state can be styled. Frames follow the pointer 1:1 under any scale or rotation, and on world-space canvases.

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
