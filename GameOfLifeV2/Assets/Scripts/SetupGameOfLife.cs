using UnityEngine;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Transforms;

using LifeComponents;

using UnityEngine.VFX;
using UnityEditor;

public struct GameOfLifeConfig : IComponentData
{
    public int NumberOfStartingSeeds;
    public int2 WorldSize;
    public uint WorldSeed;
    public float WorldUpdateRate;
    public bool LimitUpdateRate;
    public Entity AliveCell;
    public Entity DeadCell;
    public float3 Centre;
    public UpdateSystem SystemToUse;
    public NativeString512 particleAsset;
    public int MaxParticles;
}

public enum UpdateSystem
{
    SingleThreaded,
    MultiThreaded
}

[ConverterVersion(userName: "robj", version: 1)]
public class SetupGameOfLife : MonoBehaviour, IDeclareReferencedPrefabs, IConvertGameObjectToEntity
{
    public int NumberOfStartingSeeds = 12;
    public int2 WorldSize = new int2 { x = 100, y = 100 };
    public uint WorldSeed = 1851936439U;
    public float WorldUpdateRate = 0.1f;
    public bool LimitUpdateRate = false;
    public GameObject AliveCell;
    public GameObject DeadCell;
    public UpdateSystem SystemToUse;
    public GameObject particles;
    public int MaxParticles;

    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        // As we can't store the particle system directly on conversion
        // we are just going to store the path in the asset database
        // and extract it later to load
        var assetLocation = AssetDatabase.GetAssetPath(particles);

        var data = new GameOfLifeConfig()

        {
            NumberOfStartingSeeds = this.NumberOfStartingSeeds,
            WorldSize = this.WorldSize,
            WorldSeed = this.WorldSeed,
            WorldUpdateRate = this.WorldUpdateRate,
            LimitUpdateRate = this.LimitUpdateRate,
            AliveCell = conversionSystem.GetPrimaryEntity(this.AliveCell),
            DeadCell = conversionSystem.GetPrimaryEntity(this.DeadCell),
            Centre = this.transform.position,
            SystemToUse = this.SystemToUse,
            particleAsset = new NativeString512(assetLocation),
            MaxParticles = MaxParticles
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
            .WithName("WorldGeneration")
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
                ShouldLimitUpdates = config.LimitUpdateRate,
                CentrePoint = config.Centre,
                GridSize = config.WorldSize,
                SystemToUse = config.SystemToUse,
                ParticleAsset = config.particleAsset,
                MaxParticles = config.MaxParticles
            };

            worldSetupSystem.GenerateLifeSeed();
        }).Run();

        // Then delete all the entities so that the update doesn't run again
        EntityManager.DestroyEntity(setupQuery);
        
        return default;
    }
}
