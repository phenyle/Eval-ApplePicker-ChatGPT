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

            // var ecb = new EntityCommandBuffer(Allocator.Temp);

            // var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
            // var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            // state.Dependency.Complete();
            // new SpawnJob
            // {
            //     DeltaTime = deltaTime,
            //     ECB = ecb.AsParallelWriter()
            // }.Run();

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

                // for (var i = 0; i < settings.ValueRO.Count; i++)
                // {
                //     var apple = ecb.Instantiate(settings.ValueRO.Prefab);
                //     ecb.AddComponent<AppleTag>(apple);
                //     ecb.SetComponent(apple, LocalTransform.FromPosition(transform.ValueRO.Position));
                //
                //     ecb.AddSharedComponent(apple, new PhysicsWorldIndex());
                //     ecb.AddComponent(apple, PhysicsMass.CreateDynamic(MassProperties.UnitSphere, 1f));
                //     ecb.AddComponent(apple, new PhysicsVelocity
                //     {
                //         Linear = float3.zero,
                //         Angular = float3.zero
                //     });
                //     ecb.AddComponent(apple, new PhysicsDamping
                //     {
                //         Linear = 0.01f,
                //         Angular = 0.05f
                //     });
                //     ecb.AddComponent(apple, new PhysicsGravityFactor
                //     {
                //         Value = 1f
                //     });
                // }

                settings.ValueRW.Timer = settings.ValueRO.Delay;
            }

            // ecb.Dispose();
        }
    }
}
