﻿using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using LifeComponents;
using Unity.Transforms;
using Unity.Collections;
using System.Collections.Generic;

namespace LifeUpdateSystem
{
    [UpdateInGroup(typeof(LifeUpdateGroup))]
    [UpdateAfter(typeof(GameOfLifeWorldUpdateSystem))]
    public class LifeUpdateSystemMultiThread : JobComponentSystem
    {

        EntityQuery updateFinder;

        CellStateUpdateCommandBufferSystem cmdBufferSystem;

        protected override void OnCreate()
        {
            updateFinder = GetEntityQuery(
                typeof(WorldUpdateTracker),
                typeof(ShouldUpdateTag),
                typeof(MultiThreadUpdateTag),
                typeof(WorldDetails)
                );

            cmdBufferSystem = World.GetOrCreateSystem<CellStateUpdateCommandBufferSystem>();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {

            // Grab any and all render details in the world
            var sharedComponentData = new List<WorldDetails>();
            EntityManager.GetAllUniqueSharedComponentData(sharedComponentData);
            var updateJobs = new JobHandle();

            // Grab a command buffer to store the remove comamnds in for the update
            // filter entities.
            var removeCmds = cmdBufferSystem.CreateCommandBuffer();

            foreach (var worldDetails in sharedComponentData)
            {

                updateFinder.SetSharedComponentFilter(worldDetails);
                if (updateFinder.CalculateEntityCount() == 0)
                    continue;
              
                // Grab a command buffer for this update function to store commands in
                var cmds = cmdBufferSystem.CreateCommandBuffer().ToConcurrent();

                // And grab the component data for the entities with AliveCells attached
                var aliveCells = GetComponentDataFromEntity<AliveCell>(isReadOnly: true);

                // We need to make a local copy of the entity IDs for the renderable
                // prefabs as we can't access worldDetails directly in the lambda as
                // it's a managed type.
                Entity DeadRenderer = worldDetails.DeadRenderer;
                Entity AliveRenderer = worldDetails.AliveRenderer;

                var updateHandle = Entities
                    .WithSharedComponentFilter(worldDetails)
                    .WithReadOnly(aliveCells)
                    .ForEach((Entity entity, int entityInQueryIndex,
                    in Renderable mesh, in LifeCell lifeCell, in DynamicBuffer<EntityElement> buffer, in Translation translation) =>
                {

                    // First we loop over all those around us and count up how many are alive...
                    int aliveCount = 0;
                    foreach (var neighbour in buffer)
                    {
                        // Check in the component data array to see if our
                        // neighbour is alive or not
                        if (aliveCells.Exists(neighbour))
                        {
                            aliveCount++;
                        }
                    }

                    // Then we see if we are alive or not and either stay alive,
                    // die or come to life as required.
                    // As we are executing on multiple threads we need to store the changes in the command buffer
                    // for later execution
                    if (aliveCells.Exists(entity))
                    {
                        if (!(aliveCount == 2 || aliveCount == 3))
                        {
                            // Components still can't be removed while iterating, however for this system we can use the
                            // command buffer we created earlier which will be executed once this update system has finished running.
                            cmds.RemoveComponent<AliveCell>(entityInQueryIndex, entity);
                            // and then do a couple of flips of data so that the rendering is in sync
                            cmds.SetComponent(entityInQueryIndex, entity, new Translation { Value = translation.Value - new float3(.0f, 1.0f, .0f) });

                            // clean up the old mesh value and swap to the new renderable
                            var renderable = cmds.Instantiate(entityInQueryIndex, DeadRenderer);
                            cmds.AddComponent(entityInQueryIndex, renderable, new Parent { Value = entity });
                            cmds.DestroyEntity(entityInQueryIndex, mesh.value);
                        }
                    }
                    else if (aliveCount == 3)
                    {
                        // Add the alive tag
                        cmds.AddComponent(entityInQueryIndex, entity, new AliveCell { });

                        // and then do a couple of flips of data so that the rendering is in sync
                        cmds.SetComponent(entityInQueryIndex, entity, new Translation { Value = translation.Value + new float3(.0f, 1.0f, .0f) });

                        // clean up the old mesh value and swap to the new renderable
                        var renderable = cmds.Instantiate(entityInQueryIndex, AliveRenderer);
                        cmds.AddComponent(entityInQueryIndex, renderable, new Parent { Value = entity });
                        cmds.DestroyEntity(entityInQueryIndex, mesh.value);
                    }

                }).Schedule(inputDeps);

                updateHandle.Complete();

                cmdBufferSystem.AddJobHandleForProducer(updateHandle);
                updateJobs = JobHandle.CombineDependencies(updateJobs, updateHandle);

                var updateFilter = updateFinder.GetSingletonEntity();
                removeCmds.RemoveComponent<ShouldUpdateTag>(updateFilter);
            }

            return updateJobs;
        }
    }
}