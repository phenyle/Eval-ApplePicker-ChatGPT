using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DOTS
{
    public partial class AppleSpawnerSystem : SystemBase
    {
        public Action<float3> SpawnApple;

        protected override void OnUpdate()
        {
            var deltaTime = SystemAPI.Time.DeltaTime;

            foreach (var (transform, settings) in SystemAPI.Query<RefRO<LocalTransform>, RefRW<AppleDropSettings>>())
            {
                settings.ValueRW.Timer = settings.ValueRO.Timer - deltaTime;

                if (settings.ValueRO.Timer > 0f) continue;

                settings.ValueRW.Counter = settings.ValueRO.Counter + 1;

                if (settings.ValueRO.Counter % 20 == 0 && settings.ValueRO.Count < 1000)
                {
                    settings.ValueRW.Count = settings.ValueRO.Count + 5;
                }

                SpawnApple?.Invoke(transform.ValueRO.Position);

                settings.ValueRW.Timer = settings.ValueRO.Delay;
            }
        }
    }
}
