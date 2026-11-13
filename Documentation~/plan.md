# Plan: `com.threads.uiframes`, interactive UI frames for UI Toolkit and uGUI

## Context

`BRIEF-TouchUI.md` (project root) describes a UI Toolkit-only, touch-only package for drawers, card strips, card decks and joystick views. The user is correcting it in three ways:

- **It covers both UI systems.** UI Toolkit (UXML/USS) and uGUI (Canvas and GameObjects) are both first-class.
- **It is not touch-only.** Frames must also work with mouse, keyboard and gamepads. This is why behaviour is declared through markup/CSS-style tags: the tags say *what* a frame does, and separate per-platform **controllers** decide *how* each input device drives it.
- **TouchController is optional.** It supplies the touch controller, but the core package must not need it.

Decisions made with the user:
- **Package:** `com.threads.uiframes`, namespace `Threads.UiFrames`, display name "UI Frames".
- **Repo:** a new public repo, added as a git submodule at `Packages/com.threads.uiframes` (the same workflow as TouchController).
- **Input:** a semantic-command layer. Frames only understand commands, and controllers turn device input into commands.
- **uGUI tags:** a class-list component plus a ScriptableObject stylesheet that uses the same vocabulary as USS.
- **TouchController:** an optional dependency, compiled in through `versionDefines`.

The brief's rules still apply: no singletons, explicit Startup/Shutdown, Unity globals only through a host interface, New Input System only, dp thresholds, everything unit tested, tests run on a scratch copy, commit or push only when asked, and commits use the no-reply email. The only rule that changes is "Mobile touch only".

## Architecture (assemblies)

| Assembly | Depends on | Contents |
|---|---|---|
| `Threads.UiFrames` (core) | nothing Unity-UI specific | `FrameRegistry`, `IFrameKind`, `IFrameElement`, `FrameProperty<T>`, the built-in kinds and frame views (`DrawerFrame`, `StripFrame`, `DeckFrame`) over `IFrameElement`, plain C# models (`DrawerModel`, `StripModel` for inertia, rubber-band and snap, `DeckModel` for whole-card resting), `FrameTween` with an injectable `IFrameClock`, `FrameCommand` and `IFrameCommandTarget`, `FrameStyle` (the resolved property set) with `FrameStyleParser`, and `IFrameHost` (time, dp scale) |
| `Threads.UiFrames.Toolkit` | core, UIElements | `UiFramesDocument` (a MonoBehaviour on the UIDocument): scans for marker classes and `Rescan()`s, reads custom properties in `CustomStyleResolvedEvent`, holds the views (`DrawerView`, `StripView` and `DeckView` over VisualElements), and ships `UiFrames.uss` |
| `Threads.UiFrames.UGUI` | core, UnityEngine.UI | `UiFrameClasses` (component, class list), `UiFrameStyleSheet` (an SO of ordered rules: class selector → property map, later rules win), `UiFramesCanvas` binder, RectTransform views, and a default stylesheet asset |
| `Threads.UiFrames.Input` | core, Input System | Controllers. `PointerController` drives drags and wheel scrolling from the UI system's own pointer events (mouse or pen; it ignores `pointerType == touch` while a touch controller is active). `NavigationController` turns Input System actions (keyboard or gamepad: navigate, submit, cancel, page and scroll) into commands for the focused frame. Ships a default `.inputactions` |
| `Threads.UiFrames.TouchController` | core, both adapters, `com.threads.touchcontroller` (`versionDefines`, `defineConstraints`) | `TouchFrameController`: intentions per frame (`DragGesture`, `.Exclusive()`, `.StartWhen` axis-lock), `VisualElementArea` and `UiToolkitPointerSync` (from the brief), reuse of the package's `UiRaycastArea.DragOwners` and `IntentionDragRelay` for uGUI, and the joystick views (`.uf-joystick`) for both UI systems, driven by `TouchJoystick` |
| `Threads.UiFrames.Testing` and Tests (Editor and PlayMode) | | Fake clock and host. The touch tests reuse `IntentionSystemHarness`, `ScriptedTouchInput` and `IntentionEventLog` from `Threads.TouchController.Testing` |

### Shared tag vocabulary

The prefix is `uf-` (it replaces the brief's `tc-`).
- **Classes:** `uf-drawer` / `uf-drawer-handle`, `uf-card-strip` / `uf-cards`, `uf-card-deck`, `uf-no-drag`, `uf-joystick` / `uf-joystick__knob`.
- **Properties:** `--uf-edge`, `--uf-axis`, `--uf-snap`, `--uf-deceleration`, `--uf-deck-step`, `--uf-duration`, `--uf-easing`.
- **Optional input opt-outs:** `--uf-input: all|touch|pointer|navigation`. Markup never names a device; it can only restrict which controller classes apply.
- **Where values come from:** USS supplies them in UI Toolkit, and `UiFrameStyleSheet` supplies them in uGUI. Both go through the same `FrameStyleParser` and end up in one `FrameStyle`.
- **State classes:** `uf-open`, `uf-dragging`, `uf-focused`. These are real USS classes in UI Toolkit. In uGUI they are added to `UiFrameClasses` and raise an event, so designers can hook up visuals.

### Extensibility (decided 2026-11-13)

Other packages can add their own tags and behaviours. The built-in drawer, strip and deck are registered through the same API, so they get no special treatment.

- **Properties:** `FrameProperty<T>` is a typed definition: a `--name`, a parser and an error message. `FrameStyle` is a bag of property values read with `style.TryGet(prop, out v)`. Properties are declared up front because UI Toolkit can only read custom properties whose names it already knows. Each binder reads every declared name.
- **Kinds:** `IFrameKind` has a `MarkerClass`, the `Properties` it reads, and `Create(FrameContext)`, which returns an `IFrameBehaviour`. A behaviour is a command target with `ApplyStyle`, `OnLayoutChanged`, `Update` and `Dispose`.
- **Elements:** `IFrameElement` is one abstraction over a VisualElement or a RectTransform, so a behaviour works in both UI systems unchanged. It provides:
  - the class list and state classes;
  - the layout rect in dp (relative to the parent, y down, untranslated);
  - children and the native object;
  - `SetTranslationDp` and `SetVisible`.

  The built-in frame views live in the core on top of this interface. Each adapter only implements `IFrameElement`, plus the scanning and style reading.
- **Commands:** `FrameCommandId` is an open id. The built-in ids are static fields, and any package can define new ones (`new FrameCommandId("mypkg.spin")`). Commands carry `Index`, `DeltaDp`, `VelocityDps` and an optional `Payload`. Controller binding tables map input to ids and can be extended too.
- **Registry:** a `FrameRegistry` instance is built by the bootstrap (`FrameRegistry.WithBuiltIns()`, then `.Add(kind)`) and passed to the binders and controllers. There is no singleton and no reflection scan.
- **Conflict rule (user decision):** the user controls precedence through registration order, and the **most recent registration wins**.
  - Each element gets one behaviour: the most recently registered kind whose marker class it has.
  - Registering a kind for a marker class that is already taken replaces the earlier kind, which is how a user overrides a built-in.
  - Two properties with the same name must have the same type, or the registration fails.

### Wiring (no singletons)

- A bootstrap creates the binder(s) and controllers and calls `Startup()` and `Shutdown()` itself.
- Controllers get references to the binder's frame registry. A `UiFramesRunner` MonoBehaviour gives the default wiring. It has a platform profile listing which controllers to create (touch / pointer / navigation), and it can optionally reference an `IntentionRunner`.

## Phases

0. **Scaffold and spikes.**
   - Scaffold:
     - Create the repo and submodule, `package.json` (unity 6000.3, deps inputsystem 1.20, ugui 2.0), asmdefs and LICENSE.
     - Set the no-reply email locally.
     - Confirm with the user before creating the GitHub repo.
   - Spikes:
     - the brief's UI Toolkit spikes (touch → `pointerId` with and without an EventSystem; whether `PointerCancelEvent` resets Clickable, sliders and ScrollView; whether `:hover` sticks; when the TextField keyboard opens; how to notice elements added at runtime);
     - whether touch *also* arrives as UI Toolkit and uGUI pointer events, so pointer and touch controllers don't handle it twice;
     - how UI Toolkit focus and navigation events, and uGUI `EventSystem` selection, interact with `NavigationController`.
1. **Core.** Models, tween, commands and the style parser, with edit-mode tests (fake clock; snap 160 ms, drawer 220 ms, ease-out; no animation while dragging; the deck always rests on whole cards).
2. **UI Toolkit adapter.** The binder, the views and the default USS. PlayMode tests drive the views through commands.
3. **uGUI adapter.** `UiFrameClasses`, `UiFrameStyleSheet` (with a custom inspector for its rules), the binder and the views, with PlayMode tests.
4. **Controllers.**
   - `PointerController` and `NavigationController`, tested with `InputTestFixture` mouse, keyboard and gamepad.
   - `TouchFrameController`, tested with the TouchController harness and a simulated touchscreen.
   - The default-control conflicts in TouchController's `plan.md` §10 (sliders, ScrollRect, InputField) must be covered for both UI systems.
5. **Joystick views** for UI Toolkit and uGUI, in the TouchController assembly.
6. **Samples and docs.** One demo scene per UI system, both runnable with touch, mouse and gamepad. Also `Documentation~/Usage.md`, a CHANGELOG, and a `link-samples` junction like TouchController's.

## Housekeeping

- Write a corrected `plan.md` into the new repo, covering both UI systems, multi-input, the command layer and the `uf-` vocabulary.
- Mark `BRIEF-TouchUI.md` as superseded and point it at that plan.
- Update the `ui-scope-split` memory.

## Verification

- Use a robocopy of the project in the scratchpad and run Unity `-batchmode -runTests` for EditMode and PlayMode, with coverage that includes PlayMode.
- Run each sample scene in the Editor with mouse, keyboard, gamepad and simulated touch. Run on an Android device for real touch.
- Check the core assembly builds with TouchController removed: the touch assembly drops out and the other assemblies still compile and pass their tests.
