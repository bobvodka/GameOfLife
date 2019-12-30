using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using System.Collections.Generic;
using Unity.Jobs;

using LifeComponents;
using Unity.Transforms;

public struct GameOfLifeConfig : IComponentData
{
    public int NumberOfStartingSeeds;
    public int2 WorldSize;
    public uint WorldSeed;
    public float WorldUpdateRate;
    public bool LimitUpdateRate;
    public Entity AliveCell;
    public Entity DeadCell;
}

public class SetupGameOfLife : MonoBehaviour, IDeclareReferencedPrefabs, IConvertGameObjectToEntity
{
    public int NumberOfStartingSeeds = 12;
    public int2 WorldSize = new int2 { x = 100, y = 100 };
    public uint WorldSeed = 1851936439U;
    public float WorldUpdateRate = 0.1f;
    public bool LimitUpdateRate = false;
    public GameObject AliveCell;
    public GameObject DeadCell;

    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        var data = new GameOfLifeConfig()

        {
            NumberOfStartingSeeds = this.NumberOfStartingSeeds,
            WorldSize = this.WorldSize,
            WorldSeed = this.WorldSeed,
            WorldUpdateRate = this.WorldUpdateRate,
            LimitUpdateRate = this.LimitUpdateRate,
            AliveCell = conversionSystem.GetPrimaryEntity(this.AliveCell),
            DeadCell = conversionSystem.GetPrimaryEntity(this.DeadCell)
        };

        dstManager.AddComponentData(entity, data);
    }

    public void DeclareReferencedPrefabs(List<GameObject> referencedPrefabs)
    {
        referencedPrefabs.Add(AliveCell);
        referencedPrefabs.Add(DeadCell);
    }
}

[AlwaysSynchronizeSystem]
public class LifeConfigSystem : JobComponentSystem
{
    private EntityQuery setupQuery;

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {

        var archeType = EntityManager.CreateArchetype
            (
                typeof(LifeCell),
                typeof(EntityElement),
                typeof(Renderable),
                typeof(Translation),
                typeof(LocalToWorld)
            );

        Entities.WithStructuralChanges()
            .WithStoreEntityQueryInField(ref setupQuery)
            .WithoutBurst()
            .ForEach((Entity entity, in GameOfLifeConfig config) =>
        {
            var worldSetupSystem = new WorldSetup()
            {
                WorldSeed = config.WorldSeed,
                AliveCellPrefab = config.AliveCell,
                DeadCellPrefab = config.DeadCell,
                entityManager = EntityManager,
                NumberOfStartingSeeds = config.NumberOfStartingSeeds,
                cellArcheType = archeType,
                WorldUpdateRate = config.WorldUpdateRate,
                ShouldLimitUpdates = config.LimitUpdateRate
            };

            worldSetupSystem.GenerateLifeSeed(config.WorldSize);
        }).Run();

        // Then delete all the entities so that the update doesn't run again
        EntityManager.DestroyEntity(setupQuery);
        
        return default;
    }
}
