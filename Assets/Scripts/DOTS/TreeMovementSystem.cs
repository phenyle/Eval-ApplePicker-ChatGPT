using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DOTS
{
    public partial struct TreeMovementSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;

            foreach (var (transform, properties) in SystemAPI.Query<RefRW<LocalTransform>, RefRW<AppleTreeProperties>>())
            {
                var pos = transform.ValueRO.Position;
                pos.x += properties.ValueRO.Speed * deltaTime;
                transform.ValueRW.Position = pos;

                if (pos.x < -properties.ValueRO.LeftAndRightEdge)
                {
                    properties.ValueRW.Speed = math.abs(properties.ValueRO.Speed);
                }
                else if (pos.x > properties.ValueRO.LeftAndRightEdge)
                {
                    properties.ValueRW.Speed = -math.abs(properties.ValueRO.Speed);
                }
            }
        }
    }
}
