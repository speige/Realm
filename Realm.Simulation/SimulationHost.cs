//using Arch.Core;
using System.Runtime.InteropServices;
using Templates;
using Templates.Definitions;
using Templates.Ids;

namespace Realm.Simulation
{
    public static class SimulationHost
    {
	    public const int Value = 500;

        public static void Initialize()
        {
            // todo: Set up ECS World, load map data, etc.
        }

        public unsafe static void Tick(IntPtr inboundDataPtr, int inboundCount, IntPtr outboundDataPtr, int outboundCount)
        {
            // Core 30 Hz tick logic
            
            // 1. Read input state from Godot's memory block
            var inboundState = new ReadOnlySpan<UnitState>((void*)inboundDataPtr, inboundCount);

            // todo: run ECS systems

            // 2. Write output state back to Godot's memory block
            var outboundSnapshot = new Span<UnitState>((void*)outboundDataPtr, outboundCount);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct UnitState
        {
            public int Entity;
            public float X;
            public float Y;
            public float Z;
        }
    }
}