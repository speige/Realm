# Realm Custom Map Agents Guide

Realm is an RTS Game using Godot with C# and the Arch ECS framework.

## Map Scripting (MapScript.cs)
- Implements `IMapScript`.
- `Initialize(IGameAPI api)` is called when the map starts.
- `Update(IGameAPI api, float delta)` is called every simulation tick (30Hz).
- Use `api` to spawn units, send chat messages, define zones, set time of day, etc.

## Unit Configuration (metadata.json)
- Define custom units and properties here.
- Examples of properties: `MaxHp`, `Damage`, `Range`, `Armor`, `Speed`, `CostGold`, `PopCost`, `BuildOptions`, etc.

## Debugging
- Use the 'Attach to Realm Game Host' launch configuration in VS Code to attach the .NET debugger to the game and hit breakpoints in your `MapScript.cs`.
- Hot reloading is supported via the temp workspace sync.
