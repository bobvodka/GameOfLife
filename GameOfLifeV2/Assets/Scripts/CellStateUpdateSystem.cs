using LifeComponents;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;

namespace LifeUpdateSystem
{
    [UpdateInGroup(typeof(LifeUpdateGroup))]
    [UpdateAfter(typeof(LifeUpdateSystemMultiThread))]
    [UpdateAfter(typeof(LifeUpdateSystemSingleThread))]
    public class CellStateUpdateCommandBufferSystem : EntityCommandBufferSystem { }

    [UpdateInGroup(typeof(LifeUpdateGroup))]
    [UpdateAfter(typeof(RenderingEntitySyncSystem))]
    public class CellRendererUpdateCommandBufferSystem : EntityCommandBufferSystem { }

    [AlwaysSynchronizeSystem]
    [UpdateInGroup(typeof(LifeUpdateGroup))]
    [UpdateAfter(typeof(CellStateUpdateCommandBufferSystem))]
    public class RenderingEntitySyncSystem : SystemBase
    {
        CellRendererUpdateCommandBufferSystem renderSyncCmdBufferSystem;

        protected override void OnCreate()
        {
            renderSyncCmdBufferSystem = World.GetOrCreateSystem<CellRendererUpdateCommandBufferSystem>();
        }

        // This system syncs the new renderables to the board entities which use them.
        // This is needed because when we instanitate the entity via the command buffer while get
        // an ID back that ID is only valid in that context as it is a negative number.
        // When the command buffer is executed a new, real, Entity ID is created and the data patched
        // to point at it, however if we have already stored the ID in the component attached to a parent
        // we end up storing a bad ID and things will break.
        // the correct IDs.
        // This system executes after the new entities have been really spawned and lets us give things

        protected override void OnUpdate()
        {

            var commandBuffer = renderSyncCmdBufferSystem.CreateCommandBuffer().ToConcurrent();

            Entities.WithNone<LocalToParent>()
                .WithName("SyncRenderableData")
                .ForEach((in int entityInQueryIndex, in Entity renderable, in Parent parentCell) =>
                {
                    commandBuffer.AddComponent(entityInQueryIndex, parentCell.Value, new Renderable { value = renderable });
                    commandBuffer.AddComponent(entityInQueryIndex, renderable, new LocalToParent());
                }).ScheduleParallel();

            // Above updates the Depenency property for us, so we can use it for the command buffer system
            renderSyncCmdBufferSystem.AddJobHandleForProducer(Dependency);
        }
    };
}