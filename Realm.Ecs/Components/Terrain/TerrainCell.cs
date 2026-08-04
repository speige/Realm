namespace Realm.Ecs.Components.Terrain
{
	public enum WaterType : byte
	{
		None = 0,
		Shallow = 1,
		Deep = 2
	}

	/// <summary>
	/// Represents cell quad terrain data including tier, absolute corner heights, and per-cell discrete water state.
	/// </summary>
	public struct TerrainCell
	{
		public const float TIER_HEIGHT = 3.0f;
		public const sbyte MIN_MACRO_TIER = -16;
		public const sbyte MAX_MACRO_TIER = 16;
		public const float MIN_Y = MIN_MACRO_TIER * TIER_HEIGHT;
		public const float MAX_Y = MAX_MACRO_TIER * TIER_HEIGHT;

		private float _centerHeight;
		public float CenterHeight
		{
			get
			{
				return _centerHeight;
			}
		}


		private sbyte _macroTier;
		public sbyte MacroTier
		{
			get
			{
				return _macroTier;
			}
		}

		private void UpdateCalculations()
		{
			_centerHeight = Math.Clamp((Y_NW + Y_NE + Y_SE + Y_SW) * .25f, MIN_Y, MAX_Y);
			_macroTier = (sbyte)Math.Clamp((int)MathF.Round(_centerHeight / TIER_HEIGHT), MIN_MACRO_TIER, MAX_MACRO_TIER);
		}

		private float _yNW;
		public float Y_NW
		{
			get
			{
				return _yNW;
			}
			set
			{
				_yNW = Math.Clamp(value, MIN_Y, MAX_Y);
				UpdateCalculations();
			}
		}

		private float _yNE;
		public float Y_NE
		{
			get
			{
				return _yNE;
			}
			set
			{
				_yNE = Math.Clamp(value, MIN_Y, MAX_Y);
				UpdateCalculations();
			}
		}

		private float _ySE;
		public float Y_SE
		{
			get
			{
				return _ySE;
			}
			set
			{
				_ySE = Math.Clamp(value, MIN_Y, MAX_Y);
				UpdateCalculations();
			}
		}

		private float _ySW;
		public float Y_SW
		{
			get
			{
				return _ySW;
			}
			set
			{
				_ySW = Math.Clamp(value, MIN_Y, MAX_Y);
				UpdateCalculations();
			}
		}
		public WaterType WaterMode;

		public TerrainCell(float nw, float ne, float se, float sw, WaterType waterMode = WaterType.None)
		{
			Y_NW = nw;
			Y_NE = ne;
			Y_SE = se;
			Y_SW = sw;
			WaterMode = waterMode;
		}

		public TerrainCell(float uniformHeight, WaterType waterMode = WaterType.None)
			: this(uniformHeight, uniformHeight, uniformHeight, uniformHeight, waterMode) { }
	}
}

