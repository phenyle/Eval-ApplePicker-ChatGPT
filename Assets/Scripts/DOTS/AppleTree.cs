using Unity.Entities;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace DOTS
{
    public class AppleTree : MonoBehaviour
    {
        [Header("Apple Tree Properties")]
        public float speed;
        public float leftAndRightEdge;
        public float changeDirectionChance;

        [Header("Apple Drop Settings")]
        public GameObject applePrefab;
        public float appleDropDelay;

        private class AppleTreeBaker : Baker<AppleTree>
        {
            public override void Bake(AppleTree authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new AppleTreeProperties
                {
                    Speed = authoring.speed,
                    LeftAndRightEdge = authoring.leftAndRightEdge,
                    ChangeDirectionChance = authoring.changeDirectionChance,
                    Random = Random.CreateFromIndex((uint)entity.Index)
                });
                AddComponent(entity, new AppleDropSettings
                {
                    Prefab = GetEntity(authoring.applePrefab, TransformUsageFlags.Dynamic),
                    Delay = authoring.appleDropDelay,
                    Timer = 2f
                });
            }
        }
    }
}
