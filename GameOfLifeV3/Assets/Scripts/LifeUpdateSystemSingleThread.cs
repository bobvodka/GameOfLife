using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using LifeComponents;
using Unity.Transforms;
using System.Collections.Generic;
using System.Linq;

namespace LifeUpdateSystem
{

    [UpdateInGroup(typeof(LifeUpdateGroup))]
    [UpdateAfter(typeof(GameOfLifeWorldUpdateSystem))]
    public class LifeUpdateSystemSingleThread : SystemBase
    {
        EntityQuery updateFinder;

        CellStateUpdateCommandBufferSystem commandBufferSystem;

        protected override void OnCreate()
        {
            // This query is used to find the entity which
            // represents a Game Of Life board.
            updateFinder = GetEntityQuery(
                typeof(WorldUpdateTracker),
                typeof(ShouldUpdateTag),
                typeof(SingleThreadUpdateTag),
                typeof(WorldDetails),
                typeof(SwapRenderer)
                );

            commandBufferSystem = World.GetOrCreateSystem<CellStateUpdateCommandBufferSystem>();
        }

        protected override void OnUpdate()
        {
            // Grab a command buffer so that we can queue update commands
            var cmds = commandBufferSystem.CreateCommandBuffer();
            {
                // Grab a list of all the unique WorldDetails shared components in the world
                // this is used to find all the potential boards in the world to update
                var worldDetailsComponentData = new List<WorldDetails>();
                EntityManager.GetAllUniqueSharedComponentData(worldDetailsComponentData);
                var rendererComponentData = new List<SwapRenderer>();
                EntityManager.GetAllUniqueSharedComponentData(rendererComponentData);

                var updateDetails = worldDetailsComponentData.Zip(rendererComponentData, (wd, rd) => (wd, rd));

                foreach (var (worldDetails, renderDetails) in updateDetails)
                {
                    // Set the filter on the query to match the current world
                    // and then see if any entity matches that query 
                    updateFinder.SetSharedComponentFilter(worldDetails, renderDetails);
                    if (updateFinder.CalculateEntityCount() == 0)
                        continue;

                    var aliveCells = GetComponentDataFromEntity<AliveCell>(isReadOnly: true);

                    // Cache the functions to perform the alive/dead checks
                    var shouldComeToLife = worldDetails.shouldComeToLifeDie;
                    var shouldDie = worldDetails.shouldDie;

                    // The following code will be executed on the main thread
                    // so doesn't need to sync anything or return JobHandles for anyone
                    // else to sync on
                    Entities
                        .WithName("WorldUpdateNoThreads")
                        .WithSharedComponentFilter(worldDetails)
                        .ForEach((Entity entity, in Renderable mesh, in LifeCell lifeCell, in DynamicBuffer<EntityElement> buffer, in Translation translation) =>
                    {
                        // First we loop over all those around us and count up how many are alive...
                        int aliveCount = 0;
                        for(int nIdx = 0; nIdx < buffer.Length; ++nIdx)
                        {
                            var neighbour = buffer[nIdx];

                            // Check to see if our neighbour cells are alive or not
                            if (aliveCells.Exists(neighbour))
                            {
                                aliveCount++;
                            }
                        }

                        // Then we see if we are alive or not and either stay alive,
                        // die or come to life as required.
                        // All changes are staged in to a command buffer - this lets us
                        // use Burst to improve the performance here
                        if (aliveCells.Exists(entity))
                        {
                            if (shouldDie.Invoke(aliveCount))
                            {
                                // Components still can't be removed while iterating, however for this system we can use the
                                // command buffer we created earlier which will be executed once this system has got done running.
                                cmds.RemoveComponent<AliveCell>(entity);
                                // and then do a couple of flips of data so that the rendering is in sync
                                cmds.SetComponent(entity, new Translation { Value = translation.Value - new float3(.0f, 1.0f, .0f) });

                                // Spawn a new renderable and link it to the cell on the board,
                                // and clean up the old one
                                var renderable = cmds.Instantiate(renderDetails.DeadRenderer);
                                cmds.AddComponent(renderable, new Parent { Value = entity });
                                cmds.DestroyEntity(mesh.value);
                            }
                        }
                        else if (shouldComeToLife.Invoke(aliveCount))
                        {
                            // Add the alive tag
                            cmds.AddComponent(entity, new AliveCell { });

                            // and then do a couple of flips of data so that the rendering is in sync
                            cmds.SetComponent(entity, new Translation { Value = translation.Value + new float3(.0f, 1.0f, .0f) });

                            // Spawn a new renderable and link it to the cell on the board,
                            // and clean up the old one
                            var renderable = cmds.Instantiate(renderDetails.AliveRenderer);
                            cmds.AddComponent(renderable, new Parent { Value = entity });
                            cmds.DestroyEntity(mesh.value);
                        }
                    }).Run();

                    // Finally clear the update tag so we don't touch this system until it
                    // requires it's next update
                    var updateFilter = updateFinder.GetSingletonEntity();
                    cmds.RemoveComponent<ShouldUpdateTag>(updateFilter);
                }                
            }
        }
    }
}

