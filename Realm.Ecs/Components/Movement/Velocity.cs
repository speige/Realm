using System.Numerics;

namespace Realm.Ecs.Components.Movement
{
	/// <summary>
	///     A component holding the movement velocity vector of an entity.
	/// </summary>
	internal struct Velocity
	{
		public Vector3 Value;

		public Velocity(Vector3 value)
		{
			Value = value;
		}
	}
}
