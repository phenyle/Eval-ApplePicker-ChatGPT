using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace DOTS
{
    public partial struct AppleSpawnerSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;

            var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            state.Dependency.Complete();
            new SpawnJob
            {
                DeltaTime = deltaTime,
                ECB = ecb.AsParallelWriter()
            }.Run();

            // foreach (var (transform, settings) in SystemAPI.Query<RefRO<LocalTransform>, RefRW<AppleDropSettings>>())
            // {
            //     settings.ValueRW.Timer = settings.ValueRO.Timer - deltaTime;
            //
            //     if (settings.ValueRO.Timer > 0f) continue;
            //
            //     for (var i = 0; i < settings.ValueRO.Count; i++)
            //     {
            //         var apple = ecb.Instantiate(settings.ValueRO.Prefab);
            //         ecb.AddComponent<AppleTag>(apple);
            //         ecb.SetComponent(apple, LocalTransform.FromPosition(transform.ValueRO.Position));
            //
            //         ecb.AddSharedComponent(apple, new PhysicsWorldIndex());
            //         ecb.AddComponent(apple, PhysicsMass.CreateDynamic(MassProperties.UnitSphere, 1f));
            //         ecb.AddComponent(apple, new PhysicsVelocity
            //         {
            //             Linear = float3.zero,
            //             Angular = float3.zero
            //         });
            //         ecb.AddComponent(apple, new PhysicsDamping
            //         {
            //             Linear = 0.01f,
            //             Angular = 0.05f
            //         });
            //         ecb.AddComponent(apple, new PhysicsGravityFactor
            //         {
            //             Value = 1f
            //         });
            //     }
            //
            //     settings.ValueRW.Timer = settings.ValueRO.Delay;
            // }
        }
    }

    [BurstCompile]
    [StructLayout(LayoutKind.Auto)]
    public partial struct SpawnJob : IJobEntity
    {
        public float DeltaTime;
        public EntityCommandBuffer.ParallelWriter ECB;

        [BurstCompile]
        private void Execute([ChunkIndexInQuery] int chunkIndex, in LocalTransform transform, ref AppleDropSettings settings)
        {
            settings.Timer -= DeltaTime;

            if (settings.Timer > 0f) return;

            for (var i = 0; i < settings.Count; i++)
            {
                var apple = ECB.Instantiate(chunkIndex, settings.Prefab);
                ECB.AddComponent<AppleTag>(chunkIndex, apple);
                ECB.SetComponent(chunkIndex, apple, LocalTransform.FromPosition(transform.Position));

                ECB.AddSharedComponent(chunkIndex, apple, new PhysicsWorldIndex());
                ECB.AddComponent(chunkIndex, apple, PhysicsMass.CreateDynamic(MassProperties.UnitSphere, 1f));
                ECB.AddComponent(chunkIndex, apple, new PhysicsVelocity
                {
                    Linear = float3.zero,
                    Angular = float3.zero
                });
                ECB.AddComponent(chunkIndex, apple, new PhysicsDamping
                {
                    Linear = 0.01f,
                    Angular = 0.05f
                });
                ECB.AddComponent(chunkIndex, apple, new PhysicsGravityFactor
                {
                    Value = 1f
                });
            }

            settings.Timer = settings.Delay;
        }
    }
}
