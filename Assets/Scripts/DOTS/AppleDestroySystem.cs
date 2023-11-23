using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

namespace DOTS
{
    public partial struct AppleDestroySystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            foreach (var (transform, entity) in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<AppleTag>().WithEntityAccess())
            {
                if (transform.ValueRO.Position.y < -20f)
                {
                    ecb.DestroyEntity(entity);
                }
            }
        }
    }
}
