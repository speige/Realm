namespace Templates
{
    using Templates.Ids;

    public interface IBaseTemplate<T> where T : struct, IBaseTemplate<T>
    {
        UniqueId<T> UniqueId { get; }
    }
}
