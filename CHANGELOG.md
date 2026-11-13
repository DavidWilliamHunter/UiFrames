# Changelog

## [0.1.3] - preview

### Fixed
- The UXML templates (CardStrip, Drawer) failed to import on a fresh machine: their comments contained "--", which XML does not allow.

## [0.1.2] - preview

### Added
- `FrameCommandId.Adjust`: an arrow across a frame's axis at the cards level is sent to the frame (±1 and the selected card) instead of doing nothing. Built-in frames ignore it; UI Menus uses it so Left/Right change a menu row's value.
- `FrameCommandId.Activate`: Enter on a card with nothing inside is also sent to the card's frame.
- `uf-pointer-only`: elements for mouse and touch only; keyboard/gamepad navigation does not stop on them or anything inside.
- `CardFrameBase.DefaultAxis`: a card frame kind can default to vertical (UI Menus' page does).

### Fixed
- UI Toolkit: controls inside a `display: none` parent were still keyboard stops (Tab reached a hidden panel's buttons).
- Entering a frame with one card skipped into that card; if the card held nothing to select, the card was also activated as though Enter had been pressed on it.

## [0.1.1] - preview (not yet tested on devices; release as 0.2.0 after device tests)

### Added
- Touch with TouchController (`Threads.UiFrames.TouchController`, compiled when `com.threads.touchcontroller` 0.4+ is installed): `UiFramesTouch` links a UI Frames Runner to an Intention Runner. It provides platform slop and density, claims fingers on frames away from game intentions (`TouchFrameClaim`), and adds `uf-joystick` frames (`JoystickKind`, `JoystickFrame`) writing the TouchSticks device. `FrameScreen` hit-tests and places elements in TouchController's screen space.
- Several fingers drag different frames at once (`FramePointerTrackers`, one tracker per pointer).
- The runner warns when UI Toolkit documents have no EventSystem (no touch then); the Runner menu adds one.
- Keys are fully configurable through the Input System:
  - `UiFramesNavigation.inputactions` ships the defaults as an editable asset.
  - The runner takes an actions asset and a map name (replacing the single-action reference), plus key repeat delay and rate.
  - A `Reverse` action (Shift) replaces the hard-coded Shift for Shift+Tab and Shift+F6.
  - Tests cover a remapped asset, runtime overrides, a rebound Reverse, and a remapped uGUI input module.
- Demos: EventSystem in the UI Toolkit scene; with TouchController installed, an Intention Runner, the bridge and a joystick, with the stick value in the status line.

## [0.1.0] - 2026-11-13

### Added
- Package scaffold: core, UI Toolkit, uGUI, Input and optional TouchController assemblies.
- Extensible tag system: `FrameRegistry` (built per app, no singleton) holding frame kinds (`IFrameKind`, keyed by marker class) and typed custom properties (`FrameProperty<T>`). The most recent registration wins: an element with several marker classes gets the newest matching kind, and re-registering a marker class or property name replaces the earlier one.
- `IFrameElement`: one view of a UI Toolkit or uGUI element, so frame behaviours (built-in or third-party) work in both UI systems.
- Open command ids (`FrameCommandId`) so packages can add their own commands.
- Built-in kinds: drawer (`uf-drawer`), card strip (`uf-card-strip`) and card deck (`uf-card-deck`), with plain C# models (`AxisMotion`, `DrawerModel`, `StripModel`, `DeckModel`).
- Testing: `ManualClock`, `FakeFrameElement`.
- `FrameSet` (live frames, keyboard focus with `uf-focused`, command bubbling to outer frames), `VelocityTracker`, and `FramePointerTracker` (slop, axis-locked frame choice, release velocity, handle clicks, wheel) shared by both UI systems.
- UI Toolkit: `ToolkitFrameBinder` (marker classes, `--*` custom properties via `CustomStyleResolvedEvent`, geometry via `GeometryChangedEvent`, `Rescan()`), `ToolkitPointerController` (mouse/pen; a drag from a button moves the frame and the button does not click), and `UiFrames.uss` defaults.
- uGUI: `UiFrameClasses` (class list, inline style, state-class events), `UiFrameStyleSheet` asset (rules with class selectors), `UguiFrameBinder` and `UguiPointerController` (drags hand off to outer ScrollRects across the frame's axis).
- `FrameNavigationController`: keyboard and gamepad (arrows/d-pad, Page Up/Down/shoulders, Home/End, Enter/Y, Esc/B, F6/Select) on Input System actions; replaceable action map.
- `UiFramesRunner`: default wiring for UI Toolkit documents and uGUI roots; `Startup(registry)` for custom kinds.
- `UnityFrameHost`: the one place Unity's clock and screen density are read.
- Demo sample: the same frames in UI Toolkit and uGUI (Threads > UI Frames > Build Demo Scenes).
- Keyboard/gamepad navigation in levels (`FrameNavigator`): frames → cards → controls, Enter in, Esc out, Tab within a level, sliders and fields adjusted only after Enter, trivial levels skipped, `Activated` for empty cards, selection following the mouse and clicks. State classes `uf-selected`, `uf-entered`, `uf-editing`; `--uf-navigation: levels|flat`. Hidden frame parts (a closed drawer) cannot take focus. UI Toolkit keys are read from the Input System so they work whether or not the panel delivers navigation events. Replaces the earlier focus follower and `--uf-focusable`.
- Focus integration (`FrameFocusFollower`, `IFrameFocusHost`, `ToolkitFocusController`, `UguiFocusController`): frames follow the UI system's own focus and navigation (`reveal` command); focus jumps to a control in view when a frame moves it away, into a drawer when it opens and back when it closes. Plain strips and decks take focus themselves (`--uf-focusable`); drawer handles are focusable and toggle on Submit. `IFrameNavigable` lets custom kinds join in.
- `FrameNavigationController` now only binds frame-level commands (page, home/end, toggle, close, cycle), leaving arrows/Submit/Cancel to the UI.
- Pointer motion is measured in each frame's local space (`IFrameElement.ToLocalDeltaDp`), and translation follows the element's own axes: scaled, rotated and world-space frames follow the pointer 1:1. Command deltas are now local layout space (y down).
- Strips and decks set `uf-horizontal` / `uf-vertical` from `--uf-axis`; no extra class is needed in markup.
- Decks hide only cards the view would cut off at rest; cards wholly outside stay focusable.
- Key repeat for keyboard/gamepad directions read from the Input System (UI Toolkit).
- Run-time UI is found automatically (`FrameEnvironment.AutoRescanSeconds`); re-applying an unchanged style or layout no longer disturbs animations.
- Setup warnings (missing `uf-cards`, mask, raycast target, layout group on the frame, drawer not absolute, deck not resting on whole cards).
- `UiFrameStateVisual` (uGUI) and `UiFrames-Theme.uss` (UI Toolkit): default looks for `uf-selected`, `uf-entered`, `uf-editing`.
- Runner events for designers: `onActivated`, `onSelectionChanged`, `onDrawerOpened`, `onDrawerClosed` (and C# events).
- `UguiFrameFactory` and GameObject > UI Frames menus; UXML templates for UI Builder.
- `--uf-visible-cards`, `--uf-arrow-step`; `IFrameNavigationHost` split from `IFrameFocusHost`.
- `Documentation~/Usage.md`.
