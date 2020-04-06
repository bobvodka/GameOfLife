using GameOfLife;
using LifeComponents;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity​Engine.AddressableAssets;

public class WorldSetup
{

    public EntityManager entityManager { set; get; }
    public EntityArchetype cellArcheType { get; set; }
    public Entity AliveCellPrefab { get; set; }
    public Entity DeadCellPrefab { get; set; }
    public uint WorldSeed { get; set; }
    public int NumberOfStartingSeeds { get; set; }
    public float WorldUpdateRate { get; set; }
    public bool ShouldLimitUpdates { get; set; }
    public float3 CentrePoint { get; set; }
    public int2 GridSize { get; set; }
    public UpdateSystem SystemToUse { get; set; }

    /*
    public UnityEngine.GameObject ParticleSystem { get; set; }

    public UnityEngine.Texture2D PositionTexture { get; set; }
    */

    public NativeString512 ParticleAsset { get; set; }
    public int MaxParticles { get; set; }

    public GameRules.RuleSet RuleSet { get; set; }

    struct StartPatternStamp
    {
        public int2[] pattern;
        public int2 location;
    }

    private WorldParticleDetails SetupParticleSystem()
    {
        var particleDetails = new WorldParticleDetails
        {
            maxParticles = MaxParticles
        };

        var particleRef = new AssetReference(ParticleAsset.ToString());
        if(particleRef.RuntimeKeyIsValid())
        {
            var loadRequest = particleRef.InstantiateAsync();
            Entity requester = entityManager.CreateEntity();
            entityManager.AddComponentData(requester, new AsyncRequestWorld
            {
                CentrePoint = CentrePoint,
                GridSize = GridSize,
                MaxParticles = MaxParticles,
                worldParticleDetails = particleDetails,
                particleGO = loadRequest
            });
        }

        return particleDetails;
    }

    public void GenerateLifeSeed()
    {
        var lifeStart = GeneratePatternStamps(NumberOfStartingSeeds, GridSize, new int2[][]
        {
                gliderTable, lightweightspaceShip, pentomino, acorn
        });

        // Generate the entities in one batch
        int entityCount = GridSize.x * GridSize.y;
        using (var cells = new NativeArray<Entity>(entityCount, Allocator.Persistent))
        {
            var worldUpdateDetails = new WorldUpdateDetails
            {
                WorldUpdateRate = this.WorldUpdateRate,
                ShouldLimitUpdates = this.ShouldLimitUpdates,
                lastUpdateTime = this.WorldUpdateRate
            };

            // Kick off the loading/setup for the particle system for this world/grid
            var particleDetails = SetupParticleSystem();

            var (shouldDieFunction, shouldComeToLifeFunction) = GameRules.GetRuleFunctions(RuleSet);
                       
            var worldDetails = new WorldDetails()
            {
                DeadRenderer = DeadCellPrefab,
                AliveRenderer = AliveCellPrefab,
                updateDetails = worldUpdateDetails,
                particleDetails = particleDetails,
                shouldDie = shouldDieFunction,
                shouldComeToLifeDie = shouldComeToLifeFunction
            };
            
            entityManager.CreateEntity(cellArcheType, cells);

            // Offset from any given entity to the surrounding enities 
            int2[] offsetTable = {
                new int2( -1, -1 ), new int2( -0, -1 ),    new int2( 1, -1 ),
                new int2( -1,  0 ), /*new int2( -0, 0 ),*/ new int2( 1, 0 ),
                new int2( -1,  1 ), new int2( 0, 1 ),      new int2( 1, 1 ),
            };

            var renderableEntitys = new NativeArray<Entity>(entityCount, Allocator.Persistent);

            // Generate adjency information for each cell
            for (int x = 0; x < GridSize.x; ++x)
            {
                for (int y = 0; y < GridSize.y; ++y)
                {
                    int2 location = new int2(x, y);

                    EntityElement[] adjacency = new EntityElement[offsetTable.Length];

                    for (int i = 0; i < offsetTable.Length; ++i)
                    {
                        int2 entityLocation = location + offsetTable[i];

                        entityLocation = WrapLocation(entityLocation, GridSize);

                        int idx = ConvertToEntityIndex(entityLocation, GridSize);

                        adjacency[i] = cells[idx];
                    }

                    int entityIdx = ConvertToEntityIndex(location, GridSize);

                    // Populate the entity information - all cells start off 'dead'
                    entityManager.SetComponentData(cells[entityIdx], new LifeCell { gridPosition = location });
                    entityManager.SetComponentData(cells[entityIdx], new Translation { Value = GetLocationAroundCentre(new float3(x, 0, y))});

                    // Setup which system will perform the update
                    if (SystemToUse == UpdateSystem.SingleThreaded)
                        entityManager.AddComponentData(cells[entityIdx], new SingleThreadUpdateTag());
                    else
                        entityManager.AddComponentData(cells[entityIdx], new MultiThreadUpdateTag());

                    // This instantiates a copy of the dead cell prefab and parents it to the cell we are processing
                    var cellMesh = entityManager.Instantiate(DeadCellPrefab);
                    entityManager.AddComponentData(cellMesh, new Parent { Value = cells[entityIdx] });
                    entityManager.AddComponentData(cellMesh, new LocalToParent());
                    // This lets us track which entity is our current renderable so we can swap it out later when we need
                    // to update our state of being
                    entityManager.SetComponentData(cells[entityIdx], new Renderable { value = cellMesh });
                    renderableEntitys[entityIdx] = cellMesh;

                    // As we can't hold an array of Entity references in a component the adjancy information is stored in a 
                    // buffer attached to the entity and populated by copying from the details we generated
                    DynamicBuffer<EntityElement> entityBuffer = entityManager.GetBuffer<EntityElement>(cells[entityIdx]);
                    entityBuffer.CopyFrom(adjacency);

                    // And finally associate some shared data so that we can swap the prefabs around later
                    // and track the world update state
                    entityManager.AddSharedComponentData(cells[entityIdx], worldDetails);
                }
            }

            SetupBoardCondition(cells, GridSize, lifeStart, renderableEntitys);
            renderableEntitys.Dispose();

            // Finally we setup an entity which tracks if this instance of the world needs to be updated or not
            // It has tags for the threading of the update system so we can correctly dispatch later

            var worldUpdateTracker = entityManager.CreateEntity();
            entityManager.AddComponentData(worldUpdateTracker, new WorldUpdateTracker());
            if (SystemToUse == UpdateSystem.SingleThreaded)
                entityManager.AddComponentData(worldUpdateTracker, new SingleThreadUpdateTag());
            else
                entityManager.AddComponentData(worldUpdateTracker, new MultiThreadUpdateTag());
            entityManager.AddSharedComponentData(worldUpdateTracker, worldDetails);
        }
    }

    #region Location mapping functions
    int2 WrapLocation(int2 location, int2 gridSize) =>
        new int2
        {
            x = location.x < 0 ? gridSize.x - 1 : location.x == gridSize.x ? 0 : location.x,
            y = location.y < 0 ? gridSize.y - 1 : location.y == gridSize.y ? 0 : location.y
        };

    int ConvertToEntityIndex(int2 location, int2 gridSize) => location.x + (location.y * gridSize.x);

    float3 GetLocationAroundCentre(float3 position) => new float3(
        position.x - ((float)GridSize.x/2.0f) + CentrePoint.x,
        position.y + CentrePoint.y,
        position.z - ((float)GridSize.y/2.0f) + CentrePoint.z);

    #endregion

    #region Setup functions
    private StartPatternStamp[] GeneratePatternStamps(int numberOfStamps, int2 gridSize, int2[][] stampPatterns)
    {
        StartPatternStamp[] stamps = new StartPatternStamp[numberOfStamps];
        var rng = new Unity.Mathematics.Random(WorldSeed);

        for (int idx = 0; idx < numberOfStamps; ++idx)
        {
            var randomStamp = rng.NextInt(stampPatterns.Length);
            var randomLocation = rng.NextInt2(gridSize);
            stamps[idx] = new StartPatternStamp
            {
                pattern = stampPatterns[randomStamp],
                location = randomLocation
            };
        }

        return stamps;
    }

    // Stamp out some starting patterns to the board flipping cells from 'dead' to 'alive' as required
    void SetupBoardCondition(NativeArray<Entity> entities, int2 gridSize, StartPatternStamp[] lifeStartPoints, NativeArray<Entity> renderables)
    {
        for (int startPoint = 0; startPoint < lifeStartPoints.Length; ++startPoint)
        {
            int2[] pattern = lifeStartPoints[startPoint].pattern;
            int2 offset = lifeStartPoints[startPoint].location;

            for (int idx = 0; idx < pattern.Length; ++idx)
            {
                var location = pattern[idx] + offset;

                if (math.all(location < gridSize))      // unlike the cells in the world for updates we aren't going to wrap here, so we just make sure we are in range
                {
                    int entityIndex = ConvertToEntityIndex(location, gridSize);
                    // This the logic that makes the cells 'alive' by simply adding a tag component
                    entityManager.AddComponent(entities[entityIndex], typeof(AliveCell));
                    // The location is adjusted slightly because it looks nicer
                    entityManager.SetComponentData(entities[entityIndex], new Translation { Value = GetLocationAroundCentre(new float3(location.x, 1, location.y)) });
                    // And we instatiate an instance of the alive cell prefab and set its parent to the cell we are processing
                    var cellMesh = entityManager.Instantiate(AliveCellPrefab);
                    entityManager.AddComponentData(cellMesh, new Parent { Value = entities[entityIndex] });
                    entityManager.AddComponentData(cellMesh, new LocalToParent());
                    entityManager.SetComponentData(entities[entityIndex], new Renderable { value = cellMesh });

                    // Clean up the one we added initially
                    entityManager.DestroyEntity(renderables[entityIndex]);
                    // and replce the entry incase we hit this cell again
                    // otherwise we end up with more than 1 child on a cell
                    renderables[entityIndex] = cellMesh;
                }
            }
        }
    }
    #endregion

    #region A few patterns found on the internets
    readonly int2[] gliderTable = {
            new int2(0,0) , new int2(1,0) , new int2(2,0) ,
            new int2(0,1),
                            new int2(1, 2),
    };
    readonly int2[] lightweightspaceShip = {
            new int2(1, 0),                                             new int2(4, 0),
            new int2(0, 1),
            new int2(0, 2),                                             new int2(4,2),
            new int2(0, 3),new int2(1, 3),new int2(2, 3),new int2(3, 3),
    };
    readonly int2[] pentomino = {
                            new int2(1, 0), new int2(2, 0),
            new int2(0, 1), new int2(1, 1),
                            new int2(1, 2)
    };
    readonly int2[] acorn = {
            new int2(1, 0),
                                            new int2(3, 1),
            new int2(0, 2),new int2(1, 2),                  new int2(4, 2),new int2(5, 2), new int2(6, 2),
    };
    #endregion
}
