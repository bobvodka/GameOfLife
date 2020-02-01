using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;
using Unity.Collections;

using LifeComponents;

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
    public class RenderingEntitySyncSystem : JobComponentSystem
    {
        CellRendererUpdateCommandBufferSystem renderSyncCmdBufferSystem;

        protected override void OnCreate()
        {
            renderSyncCmdBufferSystem = World.GetOrCreateSystem<CellRendererUpdateCommandBufferSystem>();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {

            var commandBuffer = renderSyncCmdBufferSystem.CreateCommandBuffer().ToConcurrent();

            var rendererSyncJob = Entities.WithNone<LocalToParent>()
                .WithName("SyncRenderableData")
                .ForEach((in int entityInQueryIndex, in Entity renderable, in Parent parentCell) =>
                {
                    commandBuffer.AddComponent(entityInQueryIndex, parentCell.Value, new Renderable { value = renderable });
                    commandBuffer.AddComponent(entityInQueryIndex, renderable, new LocalToParent());
                }).Schedule(inputDeps);

            renderSyncCmdBufferSystem.AddJobHandleForProducer(rendererSyncJob);

            return rendererSyncJob;
        }
    };
}