using Unity.Entities;
using Unity.Jobs;

using Unity.Transforms;


using LifeComponents;

namespace LifeUpdateSystem
{
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