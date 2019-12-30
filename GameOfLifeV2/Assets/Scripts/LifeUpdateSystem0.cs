using UnityEngine;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using LifeComponents;
using Unity.Transforms;
using Unity.Collections;
using System.Collections.Generic;

namespace LifeUpdateSystem0
{
    [AlwaysSynchronizeSystem]
    public class LifeUpdateSystem : JobComponentSystem
    {
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            using (var cmds = new EntityCommandBuffer(Allocator.TempJob))
            {
                // Grab any and all render details in the world
                var sharedComponentData = new List<WorldDetails>();
                EntityManager.GetAllUniqueSharedComponentData(sharedComponentData);

                foreach (var worldDetails in sharedComponentData)
                {
                    var updateDetails = worldDetails.updateDetails;

                    if (updateDetails == null)
                        continue;

                    if(updateDetails.ShouldLimitUpdates)
                    {
                        updateDetails.lastUpdateTime -= Time.DeltaTime;
                        if (updateDetails.lastUpdateTime > 0.0f)
                            continue;

                        updateDetails.lastUpdateTime = updateDetails.WorldUpdateRate;
                    }

                    Entities.WithStructuralChanges()
                        .WithoutBurst()
                        .WithSharedComponentFilter(worldDetails)
                        .ForEach((Entity entity, ref Renderable mesh, in LifeCell lifeCell, in DynamicBuffer<EntityElement> buffer) =>
                    {
                        // First we loop over all those around us and count up how many are alive...
                        int aliveCount = 0;
                        foreach (var neighbour in buffer)
                        {
                            // As we are on the main thread we can just ask the 
                            // EntityManager directly if they have the 'AliveCell' component
                            if (EntityManager.HasComponent<AliveCell>(neighbour))
                            {
                                aliveCount++;
                            }
                        }

                        // Then we see if we are alive or not and either stay alive,
                        // die or come to life as required.
                        // While we can update the position and share component directly
                        // the add/removal of the AliveCell tag needs to wait until after
                        // the update as completed as it impacts the results of the function
                        if (EntityManager.HasComponent<AliveCell>(entity))
                        {
                            if (!(aliveCount == 2 || aliveCount == 3))
                            {
                                // Components still can't be removed while iterating, however for this system we can use the
                                // 'PostUpdateCommands' command buffer which executes once this update function has finished running.
                                cmds.RemoveComponent<AliveCell>(entity);
                                // and then do a couple of flips of data so that the rendering is in sync
                                cmds.SetComponent(entity, new Translation { Value = new float3(lifeCell.gridPosition.x, 0, lifeCell.gridPosition.y) });

                                // clean up the old mesh value and swap to the new renderable
                                cmds.DestroyEntity(mesh.value);
                                var renderable = EntityManager.Instantiate(worldDetails.DeadRenderer);
                                mesh.value = renderable;
                                cmds.AddComponent(renderable, new Parent { Value = entity });
                                cmds.AddComponent(renderable, new LocalToParent());
                            }
                        }
                        else if (aliveCount == 3)
                        {
                            cmds.AddComponent(entity, new AliveCell { });
                        // and then do a couple of flips of data so that the rendering is in sync
                        cmds.SetComponent(entity, new Translation { Value = new float3(lifeCell.gridPosition.x, 1, lifeCell.gridPosition.y) });

                        // clean up the old mesh value and swap to the new renderable
                        cmds.DestroyEntity(mesh.value);
                            var renderable = EntityManager.Instantiate(worldDetails.AliveRenderer);
                            mesh.value = renderable;
                            cmds.AddComponent(renderable, new Parent { Value = entity });
                            cmds.AddComponent(renderable, new LocalToParent());
                        }
                    }).Run();
                }

                //Update state of cells
                cmds.Playback(EntityManager);
            }

            return default;
        }
    }
}

