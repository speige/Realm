using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Realm.Godot.ReplaySystem
{
	public static class ReplayObjectPool
	{
		private static readonly ConcurrentBag<ReplayFrame> _framePool = new();
		private static readonly ConcurrentBag<List<ReplayUnitSnapshot>> _listPool = new();
		private static readonly ConcurrentBag<List<ReplayProjectileSnapshot>> _projectileListPool = new();
		private static readonly ConcurrentBag<List<int>> _intListPool = new();

		public static ReplayFrame RentFrame()
		{
			if (_framePool.TryTake(out var frame))
			{
				if (frame.Units == null)
				{
					frame.Units = new List<ReplayUnitSnapshot>();
				}
				else
				{
					frame.Units.Clear();
				}
				if (frame.Projectiles == null)
				{
					frame.Projectiles = new List<ReplayProjectileSnapshot>();
				}
				else
				{
					frame.Projectiles.Clear();
				}
				return frame;
			}
			return new ReplayFrame
			{
				Units = new List<ReplayUnitSnapshot>(),
				Projectiles = new List<ReplayProjectileSnapshot>()
			};
		}

		public static void ReturnFrame(ReplayFrame frame)
		{
			if (frame.Units != null)
			{
				frame.Units.Clear();
			}
			if (frame.Projectiles != null)
			{
				frame.Projectiles.Clear();
			}
			_framePool.Add(frame);
		}

		public static List<ReplayUnitSnapshot> RentList()
		{
			if (_listPool.TryTake(out var list))
			{
				list.Clear();
				return list;
			}
			return new List<ReplayUnitSnapshot>();
		}

		public static void ReturnList(List<ReplayUnitSnapshot> list)
		{
			list.Clear();
			_listPool.Add(list);
		}


		public static List<ReplayProjectileSnapshot> RentProjectileList()
		{
			if (_projectileListPool.TryTake(out var list))
			{
				list.Clear();
				return list;
			}
			return new List<ReplayProjectileSnapshot>();
		}

		public static void ReturnProjectileList(List<ReplayProjectileSnapshot> list)
		{
			list.Clear();
			_projectileListPool.Add(list);
		}
		public static List<int> RentIntList()
		{
			if (_intListPool.TryTake(out var list))
			{
				list.Clear();
				return list;
			}
			return new List<int>();
		}

		public static void ReturnIntList(List<int> list)
		{
			list.Clear();
			_intListPool.Add(list);
		}
	}
}
