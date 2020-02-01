using LifeComponents;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.VFX;

namespace LifeUpdateSystem
{
    [AlwaysSynchronizeSystem]
    [UpdateInGroup(typeof(LifeUpdateGroup))]
    [UpdateAfter(typeof(CellStateUpdateCommandBufferSystem))]
    public class ParticleSpawnerSystem : JobComponentSystem
    {
        EntityQuery particleQuery;

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var sharedComponentData = new List<WorldDetails>();
            EntityManager.GetAllUniqueSharedComponentData(sharedComponentData);

            particleQuery.AddDependency(inputDeps);
            int entityCount = particleQuery.CalculateEntityCount();

            var sharedEntityDetails = new NativeArray<Entity>(entityCount, Allocator.TempJob);
            var entityLocations = new NativeArray<float2>(entityCount, Allocator.TempJob);

            var groupEntitiesJob = Entities
                .WithStoreEntityQueryInField(ref particleQuery)
                .WithName("Grab Particle Spawn Info")
                .ForEach((in int entityInQueryIndex, in NewLife lifeDetails, in Translation location) =>
                {
                    sharedEntityDetails[entityInQueryIndex] = lifeDetails.worldEntity;
                    entityLocations[entityInQueryIndex] = new float2(location.Value.x, location.Value.z);

                }).Schedule(inputDeps);

            var groupedEntities = new NativeArraySharedValues<Entity>(sharedEntityDetails, Allocator.TempJob);
            var sortEntities = groupedEntities.Schedule(groupEntitiesJob);

            sortEntities.Complete();

            // At this point we have a grouping data for all the unique types
            var countPerSharedValue = groupedEntities.GetSharedValueIndexCountArray(); // How many of each type
            var sortedIndices = groupedEntities.GetSortedIndices(); // sorted indices for each type

            int offset = 0;
            foreach (var count in countPerSharedValue)
            {
                var sharedIdx = sortedIndices[offset];
                var worldDetails = EntityManager.GetSharedComponentData<WorldDetails>(sharedEntityDetails[sharedIdx]);

                var locations = new NativeArray<float2>(worldDetails.maxParticles, Allocator.Temp);
                var particleCount = math.min(count, worldDetails.maxParticles);

                for (int idx = 0; idx < particleCount; ++idx)
                {
                    locations[idx] = entityLocations[sortedIndices[idx + offset]];
                }

                worldDetails.positionTexture.SetPixelData<float2>(locations, 0);
                worldDetails.positionTexture.Apply(updateMipmaps: false);
                var vfx = worldDetails.particleSystem.GetComponent<VisualEffect>();
                VFXEventAttribute spawnDetails = vfx.CreateVFXEventAttribute();

                //spawnDetails.SetInt("MaxParticles", particleCount);
                vfx.SetInt("MaxParticles", particleCount);
                vfx.Play(spawnDetails);

                locations.Dispose();

                offset += count;
            }

            sharedEntityDetails.Dispose();
            entityLocations.Dispose();
            groupedEntities.Dispose();

            EntityManager.DestroyEntity(particleQuery);

            return default;

        }
    }
}