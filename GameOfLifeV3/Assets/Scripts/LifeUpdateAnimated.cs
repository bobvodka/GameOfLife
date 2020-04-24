using GameOfLife;
using LifeComponents;
using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace LifeUpdateSystem
{
    [UpdateInGroup(typeof(LifeUpdateGroup))]
    [UpdateAfter(typeof(GameOfLifeWorldUpdateSystem))]
    public class LifeUpdateAnimated : SystemBase
    {
        EntityQuery updateFinder;

        CellStateUpdateCommandBufferSystem cmdBufferSystem;

        protected override void OnCreate()
        {
            updateFinder = GetEntityQuery(
                typeof(WorldUpdateTracker),
                typeof(ShouldUpdateTag),
                typeof(MultiThreadUpdateTag),
                typeof(WorldDetails),
                typeof(AnimatedRenderer)
                );

            cmdBufferSystem = World.GetOrCreateSystem<CellStateUpdateCommandBufferSystem>();
        }

        protected override void OnUpdate()
        {

            // Grab any and all render details in the world
            var worldDetailsComponentData = new List<WorldDetails>();
            EntityManager.GetAllUniqueSharedComponentData(worldDetailsComponentData);
            var rendererComponentData = new List<AnimatedRenderer>();
            EntityManager.GetAllUniqueSharedComponentData(rendererComponentData);
            var updateJobs = new JobHandle();

            // Grab a command buffer to store the remove comamnds in for the update
            // filter entities.
            var removeCmds = cmdBufferSystem.CreateCommandBuffer();

            var updateDetails = worldDetailsComponentData.Zip(rendererComponentData, (wd, rd) => (wd, rd));

            foreach (var (worldDetails, renderDetails) in updateDetails)
            {

                updateFinder.SetSharedComponentFilter(worldDetails, renderDetails);
                if (updateFinder.CalculateEntityCount() == 0)
                    continue;

                // Grab a command buffer for this update function to store commands in
                var cmds = cmdBufferSystem.CreateCommandBuffer().ToConcurrent();
                var particleCmds = cmdBufferSystem.CreateCommandBuffer().ToConcurrent();

                var updateFilter = updateFinder.GetSingletonEntity();

                // We need to take a copy of the alive/dead colours as we can't access the shared data in the job
                // Note - we could attach this per entity and avoid this extra copy but that is a trade of extra 
                // memory per entity vs having to access like this.
                float4 aliveColour = renderDetails.AliveColour;
                float4 deadColour = renderDetails.DeadColour;

                float now = (float)Time.ElapsedTime;
                float delta = worldDetails.updateDetails.WorldUpdateRate;

                var shouldComeToLife = worldDetails.shouldComeToLifeDie;
                var shouldDie = worldDetails.shouldDie;
                bool shouldSpawnParticles = worldDetails.particleDetails.particleSystem != null;

                var updateHandle = Entities
                    .WithName("WorldUpdateThreaded")
                    .WithSharedComponentFilter(worldDetails)
                    .ForEach((Entity entity, int entityInQueryIndex,
                    in Renderable mesh, in LifeCell lifeCell, in DynamicBuffer<EntityElement> buffer, in Translation translation) =>
                    {

                        // First we loop over all those around us and count up how many are alive...
                        int aliveCount = 0;
                        for (int nIdx = 0; nIdx < buffer.Length; ++nIdx)
                        {
                            var neighbour = buffer[nIdx];

                            // Check in the component data array to see if our
                            // neighbour is alive or not
                            if (HasComponent<AliveCell>(neighbour))
                            {
                                aliveCount++;
                            }
                        }

                        bool hasAnimInfo = HasComponent<MaterialAnimationInfo>(mesh.value);


                        // Then we see if we are alive or not and either stay alive,
                        // die or come to life as required.
                        // As we are executing on multiple threads we need to store the changes in the command buffer
                        // for later execution
                        if (HasComponent<AliveCell>(entity))
                        {
                            if (shouldDie.Invoke(aliveCount))
                            {
                                // Components still can't be removed while iterating, however for this system we can use the
                                // command buffer we created earlier which will be executed once this update system has finished running.
                                cmds.RemoveComponent<AliveCell>(entityInQueryIndex, entity);

                                var animInfo = new MaterialAnimationInfo
                                {
                                    startColour = aliveColour,
                                    endColour = deadColour,
                                    startPosition = 1.0f,
                                    endPosition = 0.0f,
                                    endTime = now + delta,
                                    totalTime = delta
                                };

                                // Next setup an animation
                                if (hasAnimInfo)
                                    cmds.SetComponent(entityInQueryIndex, mesh.value, animInfo);
                                else
                                    cmds.AddComponent(entityInQueryIndex, mesh.value, animInfo);
                            }
                            else if (hasAnimInfo)
                            {
                                cmds.RemoveComponent<MaterialAnimationInfo>(entityInQueryIndex, mesh.value);
                            }
                        }
                        else if (shouldComeToLife.Invoke(aliveCount))
                        {
                            // Add the alive tag
                            cmds.AddComponent(entityInQueryIndex, entity, new AliveCell { });

                            var animInfo = new MaterialAnimationInfo
                            {

                                startColour = deadColour,
                                endColour = aliveColour,
                                startPosition = 0.0f,
                                endPosition = 1.0f,
                                endTime = now + delta,
                                totalTime = delta
                            };

                            // Next setup an animation
                            if (hasAnimInfo)
                                cmds.SetComponent(entityInQueryIndex, mesh.value, animInfo);
                            else
                                cmds.AddComponent(entityInQueryIndex, mesh.value, animInfo);
                            /*

                            // Tag that we want a particle system
                            if (shouldSpawnParticles)
                            {
                                var particles = particleCmds.CreateEntity(entityInQueryIndex);
                                particleCmds.AddComponent(entityInQueryIndex, particles, new NewLife { worldEntity = updateFilter });
                                particleCmds.AddComponent(entityInQueryIndex, particles, location);
                            }
                            */
                        }
                        else if (hasAnimInfo)
                        {
                            cmds.RemoveComponent<MaterialAnimationInfo>(entityInQueryIndex, mesh.value);
                        }

                    }).ScheduleParallel(Dependency);

                cmdBufferSystem.AddJobHandleForProducer(updateHandle);
                updateJobs = JobHandle.CombineDependencies(updateJobs, updateHandle);

                removeCmds.RemoveComponent<ShouldUpdateTag>(updateFilter);
            }

            // Export the dependency ourselves with the combined handles
            Dependency = updateJobs;
        }
    }
}
