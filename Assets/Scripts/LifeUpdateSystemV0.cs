using UnityEngine;
using UnityEditor;

using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Rendering;
using Unity.Transforms;

using LifeComponents;

namespace GameOfLifeV0
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [DisableAutoCreation]
    public class LifeUpdateSystem : ComponentSystem
    {
        // A few config options
        private int NumberOfStartingSeeds = 12;
        private int2 WorldSize = new int2 { x = 100, y = 100 };
        private uint WorldSeed = 1851936439U;
        private float WorldUpdateRate = 0.1f;
        private bool LimitUpdateRate = false;

        EntityArchetype defaultArcheType;

        private static RenderMesh aliveRenderMesh;
        private static RenderMesh deadRenderMesh;

        private void SetupRenderMeshDetails()
        {
            // Simplest way to get a cube in to the world
            GameObject cubeTemp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh cube = cubeTemp.GetComponent<MeshFilter>().mesh;
            GameObject.Destroy(cubeTemp);

            // ... and this will only run in the Editor for now because I didn't want to faff with Asset Bundles at this point
            Material aliveMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/AliveMaterial.mat");
            Material deadMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/DeadMaterial.mat");

            // Our two meshes - the difference is only the material used
            aliveRenderMesh = new RenderMesh
            {
                castShadows = UnityEngine.Rendering.ShadowCastingMode.On,
                receiveShadows = true,
                layer = 1,
                subMesh = 0,
                mesh = cube,
                material = aliveMaterial
            };

            deadRenderMesh = new RenderMesh
            {
                castShadows = UnityEngine.Rendering.ShadowCastingMode.On,
                receiveShadows = true,
                layer = 1,
                subMesh = 0,
                mesh = cube,
                material = deadMaterial
            };
        }

        struct StartPatternStamp
        {
            public int2[] pattern;
            public int2 location;
        }

        // This just sets up some 'stamps' for starting conditions so that the world has something to do
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

        protected override void OnCreate()
        {
            base.OnCreate();

            SetupRenderMeshDetails();

            defaultArcheType = EntityManager.CreateArchetype(
                typeof(LifeCell),
                typeof(EntityElement),
                typeof(Translation),
                typeof(RenderMesh),
                typeof(LocalToWorld)
                );

            // Setup the board and kick the simulation off
            GenerateLifeSeed(WorldSize);
        }

        // A few patterns found on the internets
        int2[] gliderTable = new int2[]
        {
            new int2(0,0) , new int2(1,0) , new int2(2,0) ,
            new int2(0,1),
                            new int2(1, 2),
        };

        int2[] lightweightspaceShip = new int2[]
        {
            new int2(1, 0),                                             new int2(4, 0),
            new int2(0, 1),
            new int2(0, 2),                                             new int2(4,2),
            new int2(0, 3),new int2(1, 3),new int2(2, 3),new int2(3, 3),
        };

        int2[] pentomino = new int2[]
        {
                            new int2(1, 0), new int2(2, 0),
            new int2(0, 1), new int2(1, 1),
                            new int2(1, 2)
        };

        int2[] acorn = new int2[]
        {
            new int2(1, 0),
                                            new int2(3, 1),
            new int2(0, 2),new int2(1, 2),                  new int2(4, 2),new int2(5, 2), new int2(6, 2),
        };

        int ConvertToEntityIndex(int2 location, int2 gridSize) => location.x + (location.y * gridSize.x);

        // Stamp out some starting patterns to the board flipping cells from 'dead' to 'alive' as required
        void SetupBoardCondition(NativeArray<Entity> entities, int2 gridSize, StartPatternStamp[] lifeStartPoints)
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
                        EntityManager.AddComponent(entities[entityIndex], typeof(AliveCell));
                        // The location is adjusted slightly because it looks nicer
                        EntityManager.SetComponentData(entities[entityIndex], new Translation { Value = new float3(location.x, 1, location.y) });
                        // And we flip the mesh to the alive render mesh
                        EntityManager.SetSharedComponentData(entities[entityIndex], aliveRenderMesh);
                    }
                }
            }
        }

        int2 WrapLocation(int2 location, int2 gridSize)
        {
            return new int2
            {
                x = location.x < 0 ? gridSize.x - 1 : location.x == gridSize.x ? 0 : location.x,
                y = location.y < 0 ? gridSize.y - 1 : location.y == gridSize.y ? 0 : location.y
            };
        }

        public void GenerateLifeSeed(int2 gridSize)
        {
            var lifeStart = GeneratePatternStamps(NumberOfStartingSeeds, gridSize, new int2[][]
            {
                gliderTable, lightweightspaceShip, pentomino, acorn
            });

            // Generate the entities in one batch
            int entityCount = gridSize.x * gridSize.y;
            var cells = new NativeArray<Entity>(entityCount, Allocator.Persistent);
            EntityManager.CreateEntity(defaultArcheType, cells);

            // Offset from any given entity to the surrounding enities 
            int2[] offsetTable = new int2[]
            {
            new int2( -1, -1 ), new int2( -0, -1 ),    new int2( 1, -1 ),
            new int2( -1,  0 ), /*new int2( -0, 0 ),*/ new int2( 1, 0 ),
            new int2( -1,  1 ), new int2( 0, 1 ),      new int2( 1, 1 ),
            };

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
                    EntityManager.SetComponentData(cells[entityIdx], new LifeCell { gridPosition = location });
                    EntityManager.SetComponentData(cells[entityIdx], new Translation { Value = new float3(x, 0, y) });
                    EntityManager.SetSharedComponentData(cells[entityIdx], deadRenderMesh);

                    // As we can't hold an array of Entity references in a component the adjancy information is stored in a 
                    // buffer attached to the entity and populated by copying from the details we generated
                    DynamicBuffer<EntityElement> entityBuffer = EntityManager.GetBuffer<EntityElement>(cells[entityIdx]);
                    entityBuffer.CopyFrom(adjacency);
                }
            }

            SetupBoardCondition(cells, gridSize, lifeStart);

            cells.Dispose();
        }

        float lastUpdateTime = 0.1f;
        protected override void OnUpdate()
        {
            if (LimitUpdateRate)
            {
                // Some update speed limiting to make the simulation look a bit nicer for humans
                lastUpdateTime -= Time.deltaTime;
                if (lastUpdateTime > 0.0f)
                    return;

                lastUpdateTime = WorldUpdateRate;
            }

            // On the main thread loop over all the entities, 
            // grabbing their details (LifeCell) and adjacency information in the DynamicBuffer
            Entities.ForEach(( Entity entity, ref LifeCell lifeCell, DynamicBuffer<EntityElement> buffer) =>
            {
                // First we loop over all those around us and count up how many are alive...
                int aliveCount = 0;
                for (int i = 0; i < buffer.Capacity; ++i)
                {
                    // As we are on the main thread we can just ask the 
                    // EntityManager directly if they have the 'AliveCell' component
                    if (EntityManager.HasComponent<AliveCell>(buffer[i]))
                    {
                        aliveCount++;
                    }
                }

                // Then we see if we are alive or not and either stay alive,
                // die or come to life as required
                if(EntityManager.HasComponent<AliveCell>(entity))
                {
                    if(!(aliveCount == 2 || aliveCount == 3))
                    {
                        // Components still can't be removed while iterating, however for this system we can use the
                        // 'PostUpdateCommands' command buffer which executes once this update function has finished running.
                        PostUpdateCommands.RemoveComponent<AliveCell>(entity);
                        // and then do a couple of flips of data so that the rendering is in sync
                        PostUpdateCommands.SetComponent(entity, new Translation { Value = new float3(lifeCell.gridPosition.x, 0, lifeCell.gridPosition.y) });
                        PostUpdateCommands.SetSharedComponent<RenderMesh>(entity, deadRenderMesh);
                    }
                }
                else if(aliveCount == 3)
                {
                    PostUpdateCommands.AddComponent(entity, new AliveCell { });
                    // and then do a couple of flips of data so that the rendering is in sync
                    PostUpdateCommands.SetComponent(entity, new Translation { Value = new float3(lifeCell.gridPosition.x, 1, lifeCell.gridPosition.y) });
                    PostUpdateCommands.SetSharedComponent<RenderMesh>(entity, aliveRenderMesh);
                }
            });
        }
    }
}
