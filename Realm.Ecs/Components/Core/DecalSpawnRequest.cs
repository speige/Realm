using System.Numerics;

namespace Realm.Ecs.Components.Core
{
	/// <summary>
	///     Represents a request to spawn a decal with specified parameters.
	/// </summary>
	internal struct DecalSpawnRequest
	{
		public string DecalId;
		public Vector3 Position;
		public float RotationY;
		public float Scale;

		public DecalSpawnRequest(string decalId, Vector3 position, float rotationY, float scale)
		{
			DecalId = decalId;
			Position = position;
			RotationY = rotationY;
			Scale = scale;
		}
	}
}
