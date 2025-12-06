namespace Templates.Definitions
{
	using Templates.Ids;
	using Templates;

	public interface IBaseEffectTemplate { }
	public interface IBaseEffectTemplate<T> : IBaseTemplate<T> where T : struct, IBaseTemplate<T>
	{
	}

	public readonly record struct BaseEffectTemplate : IBaseTemplate<BaseEffectTemplate>, IBaseEffectTemplate
	{
		public UniqueId<BaseEffectTemplate> UniqueId { get; init; }
	}
}