﻿using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using LifeComponents;
using Unity.Transforms;
using Unity.Collections;
using System.Collections.Generic;

namespace LifeUpdateSystem
{

    [AlwaysSynchronizeSystem]
    [UpdateInGroup(typeof(LifeUpdateGroup))]
    [UpdateAfter(typeof(GameOfLifeWorldUpdateSystem))]
    public class LifeUpdateSystemSingleThread : JobComponentSystem
    {
        EntityQuery updateFinder;

        CellStateUpdateCommandBufferSystem commandBufferSystem;

        protected override void OnCreate()
        {
            updateFinder = GetEntityQuery(
                typeof(WorldUpdateTracker),
                typeof(ShouldUpdateTag),
                typeof(SingleThreadUpdateTag),
                typeof(WorldDetails)
                );

            commandBufferSystem = World.GetOrCreateSystem<CellStateUpdateCommandBufferSystem>();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {

            var cmds = commandBufferSystem.CreateCommandBuffer();
            {
                // Grab any and all render details in the world
                var sharedComponentData = new List<WorldDetails>();
                EntityManager.GetAllUniqueSharedComponentData(sharedComponentData);

                foreach (var worldDetails in sharedComponentData)
                {

                    updateFinder.SetSharedComponentFilter(worldDetails);
                    if (updateFinder.CalculateEntityCount() == 0)
                        continue;

                    var updateFilter = updateFinder.GetSingletonEntity();
                    var aliveCells = GetComponentDataFromEntity<AliveCell>(isReadOnly: true);

                    Entities
                        //.WithoutBurst()
                        .WithSharedComponentFilter(worldDetails)
                        .ForEach((Entity entity, in Renderable mesh, in LifeCell lifeCell, in DynamicBuffer<EntityElement> buffer, in Translation translation) =>
                    {
                        // First we loop over all those around us and count up how many are alive...
                        int aliveCount = 0;
                        foreach (var neighbour in buffer)
                        {
                            // As we are on the main thread we can just ask the 
                            // EntityManager directly if they have the 'AliveCell' component
                            if (aliveCells.Exists(neighbour))
                            {
                                aliveCount++;
                            }
                        }

                        // Then we see if we are alive or not and either stay alive,
                        // die or come to life as required.
                        // While we can update the position and change the child entity for rendering
                        // directly the add/removal of the AliveCell tag needs to wait until after
                        // the update as completed as it impacts the results of the function
                        if (aliveCells.Exists(entity))
                        {
                            if (!(aliveCount == 2 || aliveCount == 3))
                            {
                                // Components still can't be removed while iterating, however for this system we can use the
                                // command buffer we created earlier which will be executed once this update function has finished running.
                                cmds.RemoveComponent<AliveCell>(entity);
                                // and then do a couple of flips of data so that the rendering is in sync
                                cmds.SetComponent(entity, new Translation { Value = translation.Value - new float3(.0f, 1.0f, .0f) });

                                // clean up the old mesh value and swap to the new renderable
                                var renderable = cmds.Instantiate(worldDetails.DeadRenderer);
                                cmds.DestroyEntity(mesh.value);
                                
                                cmds.AddComponent(renderable, new Parent { Value = entity });
                            }
                        }
                        else if (aliveCount == 3)
                        {
                            // Add the alive tag
                            cmds.AddComponent(entity, new AliveCell { });

                            // and then do a couple of flips of data so that the rendering is in sync
                            cmds.SetComponent(entity, new Translation { Value = translation.Value + new float3(.0f, 1.0f, .0f) });

                            // clean up the old mesh value and swap to the new renderable
                            var renderable = cmds.Instantiate(worldDetails.AliveRenderer);
                            cmds.DestroyEntity(mesh.value);
                            cmds.AddComponent(renderable, new Parent { Value = entity });
                        }
                    }).Run();

                    cmds.RemoveComponent<ShouldUpdateTag>(updateFilter);
                }                
            }

            return default;
        }
    }
}

