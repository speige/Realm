# Project: Realm

## General Project Overview:

- RTS Game using godot with c#
- Prefer functional programming paradigms where appropriate.

## Overall Coding Style:

- Do not add comments anywhere unless explicity requested
	Structs in the Realm.Ecs project should have a single XML-doc <summary /> comment at the top describing their purpose
	Everything in Realm.MapAPI should have full comprehensive XML-doc for public consumption
- Use names that are verbose enough to clearly identify their purpose
- Minimize Garbage Collection (GC) pressure by using struct-based data where possible
- Utilize high-performance .NET structures like `Span<>`, `ReadonlySpan<>`, and `StringBuffer`
- Use `System.Numerics` to enable SIMD mathematical operations where possible
- Use `yield return IEnumerable` to allow lazy evaluation where possible

### Realm.ECS:

- The core ECS classes and data must be kept internal

### Realm.MapAPI:

- Only expose safe APIs to map authors to prevent the directly manipulation of Godot nodes or internal C# ECS structures