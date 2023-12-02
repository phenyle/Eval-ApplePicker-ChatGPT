using Unity.Entities;

namespace DOTS
{
    public struct AppleDropSettings : IComponentData
    {
        public float Delay;
        public float Timer;
        public int Count;
        public int Counter;
    }
}
