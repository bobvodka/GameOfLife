using LifeComponents;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.VFX;

namespace LifeUpdateSystem
{
    [AlwaysSynchronizeSystem]
    [UpdateInGroup(typeof(LifeUpdateGroup))]
    [UpdateAfter(typeof(CellStateUpdateCommandBufferSystem))]
    public class ParticleSpawnerSystem : JobComponentSystem
    {
        EntityQuery particleQuery;

        struct ParticleDetails
        {
            public NativeArray<float2> locations;
            public int readOffset;
            public int particleCount;
            public Texture2D positionTexture;
            public VisualEffect vfx;
        }

        int MaxParticlesID;
        protected override void OnCreate()
        {
            MaxParticlesID = UnityEngine.Shader.PropertyToID("MaxParticles");
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            particleQuery.AddDependency(inputDeps);
            int entityCount = particleQuery.CalculateEntityCount();

            // Do a sweap over all the entities and extract an entity which matches the "world" they are in
            // and also convert their locations to a 2D position on the grid
            var sharedEntityDetails = new NativeArray<Entity>(entityCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var entityLocations = new NativeArray<float2>(entityCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            var groupEntitiesJob = Entities
                .WithStoreEntityQueryInField(ref particleQuery)
                .WithName("GrabParticleSpawnInfo")
                .ForEach((in int entityInQueryIndex, in NewLife lifeDetails, in Translation location) =>
                {
                    sharedEntityDetails[entityInQueryIndex] = lifeDetails.worldEntity;
                    entityLocations[entityInQueryIndex] = new float2(location.Value.x, location.Value.z);

                }).Schedule(inputDeps);

            // Next we schedule a job to process the sharedEntityDetails, i.e. the entity which indicates which world
            // a particle is spawning in.
            // This results in some structures which will let us index data in groups which is important later.
            var groupedEntities = new NativeArraySharedValues<Entity>(sharedEntityDetails, Allocator.TempJob);
            var sortEntities = groupedEntities.Schedule(groupEntitiesJob);

            sortEntities.Complete();

            // At this point we have a grouping data for all the unique types
            // These two let us access potentially unordered data in groups.
            // Where 'countPerSharedValue' tells us how many of each group we have
            // and 'sortedIndices' represents groups of indices in the original array which
            // all share the same world.
            // So if the first value of 'countPerSharedValue' is 4 it means the first
            // 4 values in 'sortedIndices' are indexes into 'sharedEntityDetails' which share
            // the same world
            var countPerSharedValue = groupedEntities.GetSharedValueIndexCountArray(); 
            var sortedIndices = groupedEntities.GetSortedIndices(); 

            // As we'll need some details per-world we are going to cache some data together
            // as we'll be processing this data a couple of times
            var particleDetails = new List<ParticleDetails>(countPerSharedValue.Length);

            // Particle Spawning is a 3 step process


            // Step 1.
            // For each 'world' we need to get
            // - the number of particles we want to spawn (needed to limit data copying)
            // - a NativeArray pointing to the texture data we want to update/write
            // - a reference to the Vfx system we want to spawn later
            int offset = 0;
            foreach (var count in countPerSharedValue)
            {
                var sharedIdx = sortedIndices[offset];
                var worldDetails = EntityManager.GetSharedComponentData<WorldDetails>(sharedEntityDetails[sharedIdx]);

                particleDetails.Add(new ParticleDetails
                {
                    locations = worldDetails.positionTexture.GetRawTextureData<float2>(),
                    vfx = worldDetails.vfx,
                    positionTexture = worldDetails.positionTexture,
                    particleCount = math.min(count, worldDetails.maxParticles),
                    readOffset = offset
                });

                offset += count;
            }
            
            // Step 2. 
            // Data copying jobs

            // For each 'world' we kick off a job using our data cached from above to copy data from the 
            // the array we created earlier with all the locations, in to the texture for this world.
            JobHandle fillJobs = default;
            foreach(var details in particleDetails)
            {
                var locations = details.locations;
                var particleCount = details.particleCount;
                var readOffset = details.readOffset;

                var fillJob = Job
                    .WithCode(() =>
                    {
                        for (int idx = 0; idx < particleCount; ++idx)
                        {
                            locations[idx] = entityLocations[sortedIndices[idx + readOffset]];
                        }
                    })
                    .WithReadOnly(entityLocations)
                    .WithReadOnly(sortedIndices)
                    .WithNativeDisableContainerSafetyRestriction(locations)
                    .WithName("FillPositionTexture")
                    .Schedule(default);
                

                fillJobs = JobHandle.CombineDependencies(fillJob, fillJobs);
            }

            // Finally we have to wait for all that copy work to complete
            fillJobs.Complete();

            // Step 3.
            // Finally we spawn things.
            // This is done by updating the data in the source texture, which was written in the job above,
            // and then using the Vfx component to tell the particle system how many particles to spawn
            // and start if off.
            // The texture was already bound on startup and so we don't have to rebind that at this point.
            foreach (var details in particleDetails)
            {
                details.positionTexture.Apply(updateMipmaps: false);
                details.vfx.SetInt(MaxParticlesID, details.particleCount);
                details.vfx.Play();
            }
            
            // Clean up NativeArrays etc we allocated.
            sharedEntityDetails.Dispose();
            entityLocations.Dispose();
            groupedEntities.Dispose();

            // This ensure we destory all the entities we created to spawn things so that we don't keep spawning
            // particles.
            EntityManager.DestroyEntity(particleQuery);

            return default;

        }
    }
}