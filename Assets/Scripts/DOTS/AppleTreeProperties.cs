using Unity.Entities;
using Unity.Mathematics;

namespace DOTS
{
    public struct AppleTreeProperties : IComponentData
    {
        public float Speed;
        public float LeftAndRightEdge;
        public float ChangeDirectionChance;
        public Random Random;
    }
}
