# Project: Realm

## General Project Overview:

- RTS Game using godot with c#
- When generating new TypeScript code, please follow the existing coding style.
- Ensure all new functions and classes have JSDoc comments.
- Prefer functional programming paradigms where appropriate.

## Overall Coding Style:

- Do not add comments anywhere unless explicity requested
	Structs in the Realm.Simulation\Templates folder should have a single XML-doc <summary /> comment at the top describing their purpose
	Everything in Realm.Simulation\API should have full comprehensive XML-doc for public consumption
- Use names that are verbose enough to clearly identify their purpose
- Minimize Garbage Collection (GC) pressure by using struct-based data where possible
- Utilize high-performance .NET structures like `Span<>`, `ReadonlySpan<>`, and `StringBuffer`
- Use `System.Numerics` to enable SIMD mathematical operations where possible
- Use `yield return IEnumerable` to allow lazy evaluation where possible

# Realm.Simulation\Templates:

Purpose: blueprints filled with metadata that can be instantiated into real stateful objects for the c# backend simulation

Coding rules:
- The 1st field should be "public readonly UniqueId<> UniqueId" and the generic param should be its own struct type
- Never contain an explicit costructor
- Use "readonly struct" not "struct"
- Use only public readonly fields
- Follow re-usable and de-coupled design principles through composition of smaller common Templates, not inheritance
- Never hard-coding game-specific terms like Gold, but use generic terms like Resource instead
- Only contain data that belongs in a blueprint for instantiating future objects (Example: MaxHealth), not contain current state (example: CurrentHealth)

### Realm.Simulation\ECS:

- The core ECS classes and data must be kept internal

### Realm.Simulation\API:

- Only expose a safe APIs to map authors to prevent the directly manipulation of Godot nodes or internal C# ECS structures
- Use classes like `Unit` or `Player` as wrappers to the underlying ECS data whose methods orchestrate multi-step ECS manipulation and data validation
- Designed as a Fluent API around the wrappers for safely querying the ECS