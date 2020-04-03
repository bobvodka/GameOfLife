using LifeComponents;
using System.Collections.Generic;
using Unity.Entities;

[AlwaysSynchronizeSystem]
[UpdateInGroup(typeof(LifeUpdateGroup))]
public class GameOfLifeWorldUpdateSystem : SystemBase
{
    EntityQuery updateFinder;

    protected override void OnCreate()
    {
        updateFinder = GetEntityQuery(typeof(WorldUpdateTracker), typeof(WorldDetails));
    }

    protected override void OnUpdate()
    {
        // Grab all the shared component data for the worlds
        // This works out to be one per world group
        var sharedComponentData = new List<WorldDetails>();
        EntityManager.GetAllUniqueSharedComponentData(sharedComponentData);

        // Loop over each world share component...
        foreach (var worldDetails in sharedComponentData)
        {
            // .. and extract the update details which contains our
            // update rate and time information
            var updateDetails = worldDetails.updateDetails;

            if (updateDetails == null)
                continue;

            // If we should limit our update rate then we need to check each
            // frame ...
            if (updateDetails.ShouldLimitUpdates)
            {
                updateDetails.lastUpdateTime -= Time.DeltaTime;
                if (updateDetails.lastUpdateTime > 0.0f)
                    continue;

                updateDetails.lastUpdateTime = updateDetails.WorldUpdateRate;
            }

            // ... and if it's time to update this world then we tag the
            // entity which represents this world/board so we can process it later
            updateFinder.SetSharedComponentFilter(worldDetails);

            var updateTracker = updateFinder.GetSingletonEntity();
            EntityManager.AddComponentData(updateTracker, new ShouldUpdateTag());
                
        }
    }
}