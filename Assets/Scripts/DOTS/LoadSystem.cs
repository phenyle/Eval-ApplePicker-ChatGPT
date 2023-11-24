using Unity.Burst;
using Unity.Entities;
using UnityEngine;

namespace DOTS
{
    public partial struct LoadSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            foreach (var settings in SystemAPI.Query<RefRW<AppleDropSettings>>())
            {
                var numApples = settings.ValueRO.Count;

                if (settings.ValueRO.Counter % 100 == 0 && settings.ValueRO.Count < 1000)
                {
                    settings.ValueRW.Count = numApples + 10;
                }

                settings.ValueRW.Counter = settings.ValueRO.Counter + 1;

                Debug.Log("numApples: " + numApples + " fps: " + 1f / SystemAPI.Time.DeltaTime);
            }
        }
    }
}
