using Unity.Entities;
using Unity.Jobs;
using LifeComponents;
using System.Collections.Generic;

[AlwaysSynchronizeSystem]
[UpdateInGroup(typeof(LifeUpdateGroup))]
public class GameOfLifeWorldUpdateSystem : JobComponentSystem
{
    EntityQuery updateFinder;

    protected override void OnCreate()
    {
        updateFinder = GetEntityQuery(typeof(WorldUpdateTracker), typeof(WorldDetails));
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        var sharedComponentData = new List<WorldDetails>();
        EntityManager.GetAllUniqueSharedComponentData(sharedComponentData);

        foreach (var worldDetails in sharedComponentData)
        {
            var updateDetails = worldDetails.updateDetails;

            if (updateDetails == null)
                continue;

            if (updateDetails.ShouldLimitUpdates)
            {
                updateDetails.lastUpdateTime -= Time.DeltaTime;
                if (updateDetails.lastUpdateTime > 0.0f)
                    continue;

                updateDetails.lastUpdateTime = updateDetails.WorldUpdateRate;

                updateFinder.SetSharedComponentFilter(worldDetails);

                var updateTracker = updateFinder.GetSingletonEntity();
                EntityManager.AddComponentData(updateTracker, new ShouldUpdateTag());
                
            }
        }

        return default;
    }
}