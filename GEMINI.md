# Project: Realm

## General Project Overview:

- RTS Game using godot with c#
- Prefer functional programming paradigms where appropriate.

## Overall Coding Style:

- All code, variable names, and comments should be written in english
- Do not add comments anywhere unless explicity requested
	Structs in the Realm.Ecs project should have a single XML-doc <summary /> comment at the top describing their purpose
	Everything in Realm.MapAPI should have full comprehensive XML-doc for public consumption
- Do not add regions
- Use names that are verbose enough to clearly identify their purpose
- Minimize Garbage Collection (GC) pressure by using struct-based data where possible
- Utilize high-performance .NET structures like `Span<>`, `ReadonlySpan<>`, and `StringBuffer`
- Use `System.Numerics` to enable SIMD mathematical operations where possible
- Use `yield return IEnumerable` to allow lazy evaluation where possible

## Godot specific coding rules:
- 2d button : always specify icon_max_width property

### Realm.ECS:
- The core ECS classes and data must be kept internal.
- Keep system logic separated from presentation. Physics process query loops in `GameHost.cs` should inspect and manipulate ECS data via components, updating `Unit3D` / `Prop3D` nodes accordingly.

### Realm.MapAPI:
- Only expose safe APIs to map authors to prevent the direct manipulation of Godot nodes or internal C# ECS structures.
- All map scripting operations should strictly proxy through interfaces (like [IGameAPI](file:///C:/temp/Realm/Realm.MapAPI/IGameAPI.cs) and [IUnit](file:///C:/temp/Realm/Realm.MapAPI/IUnit.cs)). Implementations (e.g. `UnitWrapper`) must hide the underlying `Arch.Core.Entity` and raw Godot `Node` references.

## AI "Vibe" Coding & Maintenance Instructions:
- Avoid using proprietary or copyrighted terms from other games
- **Low-GC Architecture**: The game simulation tick runs at 30Hz. Avoid allocating objects, using lambdas that capture variables, or calling `new` inside query loops. Prefer reusing collections, employing object pools, and using `struct` components.
- **ECS-Godot Coupling**: `GameHost.cs` acts as the bridge. Maintain unity by storing `Unit3D` or `Prop3D` references directly as components on their respective ECS entities, allowing fast lookup during ECS queries.
- **Styling & Constraints**:
  - Never add code comments unless explicitly requested.
  - Every struct in `Realm.Ecs` must have exactly one `<summary />` XML documentation tag describing its purpose.
  - Every public member/type in `Realm.MapAPI` must have complete, detailed XML-doc comments for Intellisense inside embedded VSCode.
  - Never use `#region` blocks.
  - Keep names descriptive and verbose (avoid cryptic abbreviations).
  - Use `System.Numerics` for vector arithmetic and SIMD performance optimization.
  - Utilize `Span<T>` and `ReadOnlySpan<T>` when transferring structural buffers between Godot and C#.