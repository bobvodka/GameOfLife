using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

using LifeComponents;
using Unity.Transforms;
using System.Collections.Generic;

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

    struct StartPatternStamp
    {
        public int2[] pattern;
        public int2 location;
    }

    public void GenerateLifeSeed(int2 gridSize)
    {
        var lifeStart = GeneratePatternStamps(NumberOfStartingSeeds, gridSize, new int2[][]
        {
                gliderTable, lightweightspaceShip, pentomino, acorn
        });

        // Generate the entities in one batch
        int entityCount = gridSize.x * gridSize.y;
        using (var cells = new NativeArray<Entity>(entityCount, Allocator.Persistent))
        {
            var worldUpdateDetails = new WorldUpdateDetails
            {
                WorldUpdateRate = this.WorldUpdateRate,
                ShouldLimitUpdates = this.ShouldLimitUpdates,
                lastUpdateTime = this.WorldUpdateRate
            };

            var worldDetails = new WorldDetails()
            {
                DeadRenderer = DeadCellPrefab,
                AliveRenderer = AliveCellPrefab,
                updateDetails = worldUpdateDetails
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
            for (int x = 0; x < gridSize.x; ++x)
            {
                for (int y = 0; y < gridSize.y; ++y)
                {
                    int2 location = new int2(x, y);

                    EntityElement[] adjacency = new EntityElement[offsetTable.Length];

                    for (int i = 0; i < offsetTable.Length; ++i)
                    {
                        int2 entityLocation = location + offsetTable[i];

                        entityLocation = WrapLocation(entityLocation, gridSize);

                        int idx = ConvertToEntityIndex(entityLocation, gridSize);

                        adjacency[i] = cells[idx];
                    }

                    int entityIdx = ConvertToEntityIndex(location, gridSize);

                    // Populate the entity information - all cells start off 'dead'
                    entityManager.SetComponentData(cells[entityIdx], new LifeCell { gridPosition = location });
                    entityManager.SetComponentData(cells[entityIdx], new Translation { Value = new float3(x, 0, y) });

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

            SetupBoardCondition(cells, gridSize, lifeStart, renderableEntitys);
            renderableEntitys.Dispose();
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
                    entityManager.SetComponentData(entities[entityIndex], new Translation { Value = new float3(location.x, 1, location.y) });
                    // And we instatiate an instance of the alive cell prefab and set its parent to the cell we are processing
                    var cellMesh = entityManager.Instantiate(AliveCellPrefab);
                    entityManager.AddComponentData(cellMesh, new Parent { Value = entities[entityIndex] });
                    entityManager.AddComponentData(cellMesh, new LocalToParent());
                    entityManager.SetComponentData(entities[entityIndex], new Renderable { value = cellMesh });

                    // Clean up the one we added initially
                    entityManager.DestroyEntity(renderables[entityIndex]);
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
