namespace GameLogic.Core
{
    public interface IPoolable
    {
        void OnReuse();
        void OnRelease();
    }
}
