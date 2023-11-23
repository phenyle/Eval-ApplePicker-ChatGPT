using Unity.Burst;
using Unity.Entities;

namespace DOTS
{
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    public partial struct RandomDirectionSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            foreach (var properties in SystemAPI.Query<RefRW<AppleTreeProperties>>())
            {
                if (properties.ValueRW.Random.NextFloat() < properties.ValueRO.ChangeDirectionChance)
                {
                    properties.ValueRW.Speed = properties.ValueRO.Speed * -1;
                }
            }
        }
    }
}
