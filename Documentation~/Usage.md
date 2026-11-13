# UI Frames: usage

UI Frames adds **drawers**, **card strips** and **card decks** to Unity UI, for both **UI Toolkit** (UXML/USS) and **uGUI** (Canvas and GameObjects). You say *what* a frame is with tags (classes and `--uf-*` custom properties). The package decides *how* touch, mouse, keyboard and gamepad drive it.

- Unity 6000.3 or later. The New Input System is required.
- Touch comes from `com.threads.touchcontroller` when it is installed (in progress).
- Mouse, pen, keyboard and gamepad work without it.

## 1. Quick start

1. **Add a runner.** Use *GameObject > UI Frames > Runner (wire this scene)*. This creates a **UI Frames Runner** and fills in:
   - the scene's root Canvases and UIDocuments;
   - the package stylesheets for UI Toolkit (`UiFrames.uss` for layout, `UiFrames-Theme.uss` for state looks);
   - the EventSystem.
2. **Add frames.**
   - **uGUI:** *GameObject > UI Frames > Card Strip / Vertical Card Strip / Card Deck / Drawer*, then *Add Card*.
   - **UI Toolkit:** drag `CardStrip.uxml`, `CardDeck.uxml` or `Drawer.uxml` from UI Builder's Library (Project tab). You can also tag your own elements as in section 2.
3. **Press Play.**
   - **Mouse:** drag and use the wheel.
   - **Keyboard and gamepad:** Tab or arrows, Enter in, Esc out (section 5).

The runner looks for UI added, removed or re-tagged at run time every 0.5 s (`FrameEnvironment.AutoRescanSeconds`). Call `runner.Rescan()` to have it at once.

## 2. Tags

| Class | Children | Properties |
|---|---|---|
| `uf-drawer` | optional `uf-drawer-handle` | `--uf-edge: auto\|left\|right\|top\|bottom` |
| `uf-card-strip` | `uf-cards`, whose children are the cards | `--uf-axis: horizontal\|vertical`, `--uf-snap: none\|card`, `--uf-deceleration: 0.135`, `--uf-visible-cards: N` |
| `uf-card-deck` | `uf-cards`, whose children are the cards | `--uf-axis`, `--uf-deck-step: card\|page`, `--uf-visible-cards: N` |
| any frame | | `--uf-duration: 160ms`, `--uf-easing: linear\|ease-in\|ease-out\|ease-in-out`, `--uf-input: all\|none\|touch pointer navigation`, `--uf-navigation: levels\|flat`, `--uf-arrow-step: 0.1` |
| any element | | `uf-no-drag`: frame drags never start on it |

**Drawers**
- Lay a drawer out in its **open** position against its edge; the frame slides it off when closed.
- A handle placed outside the drawer's bounds stays in view, and `auto` finds the edge from it.
- A closed drawer's contents are hidden and cannot be focused.

**Decks** rest on whole cards. Either size the deck to a whole number of cards, or set `--uf-visible-cards: N` to size the cards to fit. A deck that can't rest on whole cards logs a warning.

**State classes.** The frames and the keyboard navigator set these classes, so each state can be styled:

| Class | Set on |
|---|---|
| `uf-open` | an open drawer |
| `uf-dragging` | a frame being dragged |
| `uf-selected` | the keyboard selection |
| `uf-entered` | frames and cards the keyboard is inside |
| `uf-editing` | a slider or field being adjusted |
| `uf-focused` | the frame that frame commands go to |
| `uf-horizontal` / `uf-vertical` | strips and decks, from `--uf-axis` |

### UI Toolkit

- Classes go in UXML (`class="uf-card-strip"`) and properties in USS (`--uf-snap: card;`). Quoted and unquoted values both work.
- Add `UiFrames.uss`, which gives strips and decks `overflow: hidden` and lays out `uf-cards` from `uf-vertical`.
- Optionally add `UiFrames-Theme.uss`; otherwise style the state classes yourself.

### uGUI

- Add a **UI Frame Classes** component (one class per entry) where UXML would have a class attribute.
- Properties come from **UI Frames Style Sheet** assets on the runner. Rules have class selectors (`.uf-card-strip.photos`, commas for alternatives) and apply in order, so later rules win.
- Inline declarations on the component win over all rules.
- A strip or deck needs:
  - a RectMask2D;
  - a raycast target, such as an Image (alpha 0 is fine);
  - its layout group on the `uf-cards` child, not on the frame.

  The factory and the menus set this up, and the binder warns when something is missing.
- State looks come from **UI Frame State Visual**. The runner adds one with outlines to anything the keyboard selects unless it already has one; turn that off with *uGUI Default State Visuals*. Each entry maps a state class to an outline, a tint or showing a GameObject.

## 3. Pointer: mouse, pen and touch

- **Drags:**
  - A drag past the slop moves the innermost frame whose axis matches the drag (axis lock). Otherwise the drag is left to the UI, for example an outer ScrollRect.
  - A drag that starts on a button moves the frame, and the button does not click.
  - Sliders, scroll views and text fields keep their own drags.
- **Release:** strips coast with inertia, decks step, and drawers open or close depending on the fling and how far they were dragged.
- **Wheel:** scrolls strips by 48 dp per notch (`FrameEnvironment.WheelStepDp`) and steps decks one card per notch.
- **Transforms:** scaled, rotated and world-space UI follows the pointer 1:1.

## 4. Commands

Every input becomes a `FrameCommand`:

| Command | Meaning |
|---|---|
| `open`, `close`, `toggle` | drawers |
| `next`, `previous` | one card |
| `page-next`, `page-previous` | one view |
| `home`, `end` | start or end |
| `go-to` | a card index |
| `scroll-by` | a distance |
| `drag-begin`, `drag-move`, `drag-end`, `drag-cancel` | direct manipulation |
| `reveal` | bring an element into view |

Send them from code with `frame.Execute(FrameCommand.Next)`. Find frames with `runner.Frames.Find(gameObjectOrVisualElement)`.

## 5. Keyboard and gamepad

Navigation works in levels, one navigator per UIDocument or Canvas:

| Level | Move (arrows, d-pad, stick, Tab) | Enter / A | Esc / B |
|---|---|---|---|
| Frames | between frames and controls outside frames (arrows spatially, Tab in order) | into the frame (opens a drawer) | closes an open drawer that is selected |
| Cards | along the frame's axis; the frame scrolls or steps to show the whole card | into the card | back to the frame |
| Controls | between the card's controls | presses a button; starts adjusting a slider or editing a field | stops adjusting, else back to the card |

How it behaves:
- **Skipped levels:** a frame with one card, or a card with one control, is skipped. A card with nothing inside raises **Activated**.
- **Selection follows the UI:**
  - a click moves the keyboard selection to what was clicked;
  - if the mouse or wheel scrolls the selected card out of view, the selection moves to a card in view;
  - a closed drawer is never reached by Tab or the arrows.
- **Frame commands** (any level): Page Up/Down or the shoulders, Home/End, F6 / Shift+F6 or Select to move to the next frame, gamepad Y to toggle the drawer.
- **Flat frames:** `--uf-navigation: flat` makes a frame transparent, so its controls belong to the level around it.

Events for designers are on the runner: `onActivated`, `onSelectionChanged`, `onDrawerOpened` and `onDrawerClosed` pass element names. The matching C# events (`Activated`, `SelectionChanged`, `DrawerChanged`) pass the element.

How keys arrive:
- **uGUI:** the EventSystem's Move, Submit and Cancel go to the navigator. Selectables' own automatic navigation is turned off while the runner runs, and is restored afterwards.
- **UI Toolkit:** keys are read from the Input System (`FrameNavigationController`). A panel without an EventSystem receives no keyboard navigation events at all; with one, the panel's own events are swallowed so each key acts once.
- **Rebinding:** pass your own action map with the action names in `FrameNavigationController` to rebind keys. Holding a direction repeats it after 0.5 s, every 0.1 s.

## 6. Code: your own bootstrap and your own frames

The runner is optional. Its parts are plain classes with explicit `Startup()` / `Shutdown()`, and there are no singletons:

```csharp
var registry = FrameRegistry.WithBuiltIns();
var env = UnityFrameHost.CreateEnvironment();
var frames = new FrameSet();
var binder = new UguiFrameBinder(canvasRect, registry, env, frames, styleSheets);
binder.Startup();
var pointer = new UguiPointerController(binder);
pointer.Startup();
var focus = new UguiFocusController(binder, eventSystem);
focus.Startup();
var navigator = new FrameNavigator(frames, focus);
navigator.Startup();
// each frame: binder.Update(); frames.Update(); focus.Update(); navigator.Update();
```

To add your own frame types:
1. Declare properties with `FrameProperty<T>`.
2. Implement `IFrameKind`, which gives a marker class, the properties it reads, and `Create`.
3. Implement `IFrameBehaviour`. Behaviours only see `IFrameElement`, so they work in both UI systems.
4. Implement `IFrameNavigable` to join keyboard navigation.
5. Register the kind: `FrameRegistry.WithBuiltIns().Add(new MyKind())`, then call `runner.Startup(registry)` with *Start On Enable* off.

**Precedence:** the most recent registration wins, both for an element with several marker classes and for re-registered classes or property names.

**Testing:** `ManualClock`, `FakeFrameElement` and `FakeFocusHost` (in `Threads.UiFrames.Testing`) test frames without either UI system.

## 7. Building uGUI from code

`UguiFrameFactory.CardStrip`, `CardDeck`, `AddCard` and `Drawer` build frames with the mask, raycast target, layout group, content size fitter and classes they need. The menus use the same code. The runner picks new frames up by itself.
