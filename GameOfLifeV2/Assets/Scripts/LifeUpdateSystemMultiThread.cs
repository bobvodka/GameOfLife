using Unity.Entities;
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

            foreach (var worldDetails in sharedComponentData)
            {

                updateFinder.SetSharedComponentFilter(worldDetails);
                if (updateFinder.CalculateEntityCount() == 0)
                    continue;

                var updateFilter = updateFinder.GetSingletonEntity();

                var cmds = cmdBufferSystem.CreateCommandBuffer().ToConcurrent();
                var aliveCells = GetComponentDataFromEntity<AliveCell>(isReadOnly: true);

                Entity DeadRenderer = worldDetails.DeadRenderer;
                Entity AliveRenderer = worldDetails.AliveRenderer;

                var updateHandle = Entities
                    .WithSharedComponentFilter(worldDetails)
                    .WithReadOnly(aliveCells)
                    .WithNativeDisableContainerSafetyRestriction(aliveCells)
                    .WithoutBurst()
                    .ForEach((Entity entity, int entityInQueryIndex,
                    in Renderable mesh, in LifeCell lifeCell, in DynamicBuffer<EntityElement> buffer, in Translation translation) =>
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
                            cmds.RemoveComponent<AliveCell>(entityInQueryIndex, entity);
                            // and then do a couple of flips of data so that the rendering is in sync
                            cmds.SetComponent(entityInQueryIndex, entity, new Translation { Value = translation.Value - new float3(.0f, 1.0f, .0f) });

                            // clean up the old mesh value and swap to the new renderable
                            var renderable = cmds.Instantiate(entityInQueryIndex, DeadRenderer);
                            cmds.DestroyEntity(entityInQueryIndex, mesh.value);
                            cmds.AddComponent(entityInQueryIndex, renderable, new Parent { Value = entity });
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
                        cmds.DestroyEntity(entityInQueryIndex, mesh.value);
                        cmds.AddComponent(entityInQueryIndex, renderable, new Parent { Value = entity });
                    }

                }).Schedule(inputDeps);
                updateHandle.Complete();

                cmdBufferSystem.AddJobHandleForProducer(updateHandle);
                updateJobs = JobHandle.CombineDependencies(updateJobs, updateHandle);

                EntityManager.RemoveComponent<ShouldUpdateTag>(updateFilter);
            }

            return updateJobs;
        }
    }

    [UpdateInGroup(typeof(LifeUpdateGroup))]
    [UpdateAfter(typeof(LifeUpdateSystemMultiThread))]
    [UpdateAfter(typeof(LifeUpdateSystemSingleThread))]
    public class CellStateUpdateCommandBufferSystem : EntityCommandBufferSystem { }

    [AlwaysSynchronizeSystem]
    [UpdateInGroup(typeof(LifeUpdateGroup))]
    [UpdateAfter(typeof(CellStateUpdateCommandBufferSystem))]
    public class RenderingEntitySyncSystem : JobComponentSystem
    {
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {

            Entities.WithNone<LocalToParent>()
                .WithStructuralChanges()
                .ForEach((Entity renderable, in Parent parentCell) =>
                {
                    EntityManager.AddComponentData(parentCell.Value, new Renderable { value = renderable });
                    EntityManager.AddComponentData(renderable, new LocalToParent());
                }).Run();

            return default;
        }
    };
}