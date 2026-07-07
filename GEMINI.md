# Project: Realm

## General Project Overview:
- RTS Game using Godot with C# and the Arch ECS framework.

## Overall Coding Style:

- Prefer functional programming paradigms where appropriate.
- All code, variable names, and comments must be written in English.
- Do not add comments anywhere unless explicitly requested.
	- Structs in the `Realm.Ecs` project must have a single XML-doc `<summary />` comment at the top describing their purpose.
	- Everything in `Realm.MapAPI` must have full, comprehensive XML-doc comments for public consumption.
- Do not use `#region` blocks.
- Use verbose, descriptive names that clearly identify their purpose (avoid cryptic abbreviations).
- Avoid the `sealed` keyword.

## Core 3D rendering and Engine Tick Calculations:
- Minimize Garbage Collection (GC) pressure by using struct-based data where possible.
- The game simulation tick runs at 30Hz. Absolute zero-allocation constraint inside the tick: Do not allocate objects, do not use lambdas that capture variables, and do not call `new` inside query loops. Prefer reusing collections, employing object pools, and using `struct` components.
- Utilize high-performance .NET structures like `Span<>`, `ReadOnlySpan<>`, and `StringBuffer` especially when transferring structural buffers between Godot and C#.
- Use `System.Numerics` to enable SIMD mathematical operations and vector arithmetic where possible.
- Use `yield return IEnumerable` to allow lazy evaluation where possible.

## Godot-Specific Coding Rules:
- 2D button: always specify the `icon_max_width` property.
- For any text labels that appear on screen, ensure they are translated via `LocalizationManager.cs`.

### Realm.ECS Data Layer:
- The core ECS classes and data must be kept internal.
- Keep system logic separated from presentation. Physics process query loops in `GameHost.cs` should inspect and manipulate ECS data via components, updating `Unit3D` / `Prop3D` nodes accordingly.
- Do not store Godot lifecycle elements, scene nodes, or UI references directly inside ECS components. Components must remain pure unmanaged data.
- All QueryDescription instances should be created 1x as public readonly fields in `QueryCache.cs` and re-used across the application

### Logic Services:
- Classes inheriting from Godot should have minimal orchestration logic, deferring complex logic to domain-specific services.
- Services should have the `WorldAccessor` dependency injected via their constructor and cached for their lifetime.
- Decoupled Communication: Avoid using DTOs to communicate between orchestrators (`GameHost`) and services. Communication should be minimal and limited to simple ephemeral primitive parameters and return values. All persistent shared data must be stored directly in the ECS, allowing both services and `GameHost` to query and write to the ECS independently to coordinate.
- Services should never be instantiated directly, they should always be retrieved via the global ServiceLocator during godot scene _Ready() and stored in private readonly fields.

### Realm.MapAPI:
- Only expose safe APIs to map authors to prevent the direct manipulation of Godot nodes or internal C# ECS structures.
- All map scripting operations should strictly proxy through interfaces (like `IGameAPI` and `IUnit`). Implementations (e.g. `UnitWrapper`) must hide the underlying `Arch.Core.Entity` and raw Godot `Node` references.

## AI "Vibe" Coding & Maintenance Instructions:
- Avoid using proprietary or copyrighted terms from other games
- For fast lookup during queries without breaking the PURE DATA PRINCIPLE, do not put Godot Nodes inside components. Instead, map the relationship using unique entity IDs, or look up corresponding visual nodes via a managed registry outside the ECS system arrays.
