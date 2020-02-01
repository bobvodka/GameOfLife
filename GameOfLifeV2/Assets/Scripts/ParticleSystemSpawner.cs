using LifeComponents;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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

        struct WorldParticleDetail
        {
            public WorldDetails details;
            public int offset;
            public int particleCount;
            public int copyOffset;
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
            var worldDetailCache = new List<WorldParticleDetail>(countPerSharedValue.Length);

            // Particle Spawning is a 3 step process
            

            // Step 1.
            // For each 'world' we need to get
            // - the max number of particles we can spawn  (needed later for buffer creation)
            // - the number of particles we want to spawn (needed to limit data copying)
            // - offset for writing spawn location data (as we need to write at 'max spawn count' offsets later)
            int offset = 0;
            int totalParticleCount = 0;
            foreach (var count in countPerSharedValue)
            {
                var sharedIdx = sortedIndices[offset];
                var worldDetails = EntityManager.GetSharedComponentData<WorldDetails>(sharedEntityDetails[sharedIdx]);

                worldDetailCache.Add(new WorldParticleDetail
                {
                    details = worldDetails,
                    offset = offset,
                    particleCount = math.min(count, worldDetails.maxParticles),
                    copyOffset = totalParticleCount
                });

                totalParticleCount += worldDetails.maxParticles;
                offset += count;
            }
            
            // Step 2. 
            // Data copying jobs
            // First we allocate a large buffer for all locations (zero init as we don't always use the full space)
            // so that we can store all the data in one blob and save some allocation time
            // as well as book keeping vs one array per 'world' we want to spawn in.
            // This is the main reason we had to walk all the world, we took advantage of that to cache some data
            // in advance for the next section of the copying.
            var locations = new NativeArray<float2>(totalParticleCount, Allocator.TempJob);

            // Next, for each 'world' we kick off a job using our data cached from above to copy data from the 
            // the array we created earlier with all the locations, in to a new linear buffer for this world
            JobHandle fillJobs = default;
            foreach(var world in worldDetailCache)
            {                
                var readOffset = world.offset;
                var particleCount = world.particleCount;
                var copyOffset = world.copyOffset;

                var fillJob = Job
                    .WithCode(() =>
                    {
                        for (int idx = 0; idx < particleCount; ++idx)
                        {
                            locations[idx + copyOffset] = entityLocations[sortedIndices[idx + readOffset]];
                        }
                    })
                    .WithReadOnly(entityLocations)
                    .WithReadOnly(sortedIndices)
                    .WithNativeDisableContainerSafetyRestriction(locations)
                    .WithName("FillPositionTexture")
                    .Schedule(new JobHandle());
                

                fillJobs = JobHandle.CombineDependencies(fillJob, fillJobs);
            }

            // Finally we have to wait for all that copy work to complete
            fillJobs.Complete();

            // Step 3.
            // Finally we spawn things.
            // This is done by first filling out the texture data associated with the world with the locations
            // we want to spawn in - this function requires enough data to fill the whole texture, which can hold
            // the max number of spawns per world, thus we allocated for that amount in step 2.
            // The 'world.copyOffset' ensure we copy from the right bit of source data.
            // The texture itself was bound to the Visual Effect back at world setup so we just have to
            // replace the data and we are good to go.
            // After that we use the Vfx component  to tell the particle system how many particles to spawn
            // and start if off.
            foreach (var world in worldDetailCache)
            {
                var worldDetails = world.details;
                worldDetails.positionTexture.SetPixelData<float2>(locations, 0, world.copyOffset);
                worldDetails.positionTexture.Apply(updateMipmaps: false);
                worldDetails.vfx.SetInt(MaxParticlesID, world.particleCount);
                worldDetails.vfx.Play();
            }
            
            // Clean up NativeArrays etc we allocated.
            locations.Dispose();
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