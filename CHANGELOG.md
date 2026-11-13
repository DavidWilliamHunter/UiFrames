# Changelog

## [0.1.0] - unreleased

### Added
- Package scaffold: core, UI Toolkit, uGUI, Input and optional TouchController assemblies.
- Extensible tag system: `FrameRegistry` (built per app, no singleton) holding frame kinds (`IFrameKind`, keyed by marker class) and typed custom properties (`FrameProperty<T>`). The most recent registration wins: an element with several marker classes gets the newest matching kind, and re-registering a marker class or property name replaces the earlier one.
- `IFrameElement`: one view of a UI Toolkit or uGUI element, so frame behaviours (built-in or third-party) work in both UI systems.
- Open command ids (`FrameCommandId`) so packages can add their own commands.
- Built-in kinds: drawer (`uf-drawer`), card strip (`uf-card-strip`) and card deck (`uf-card-deck`), with plain C# models (`AxisMotion`, `DrawerModel`, `StripModel`, `DeckModel`).
- Testing: `ManualClock`, `FakeFrameElement`.
