﻿using UnityEngine;
using UnityEditor;
using Unity.Jobs;
using Unity.Entities;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Rendering;
using Unity.Transforms;

using LifeComponents;

namespace GameOfLifeV2
{
    
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public class LifeUpdateSystem : JobComponentSystem
    {
        // A few config options
        private int NumberOfStartingSeeds = 12;
        private static int2 WorldSize = new int2 { x = 100, y = 100 };
        private int2 CellsPerTile = new int2 { x = WorldSize.x / 5, y = WorldSize.y / 5 };
        private uint WorldSeed = 1851936439U;
        private float WorldUpdateRate = 0.1f;
        private bool LimitUpdateRate = false;

        [BurstCompile]
        public struct CellLifeProcessing : IJobParallelFor
        {
            [ReadOnly] public NativeArray<bool> cellState;
            [ReadOnly] public int2 gridSize;
            [ReadOnly] public int2 cellsPerTile;
            [ReadOnly] public NativeArray<int2> offsetTable;

            public NativeArray<bool> newCellState;

            int ConvertToIndex(int2 location, int2 gridSize) => location.x + (location.y * gridSize.x);

            int2 GetGlobalLocation(int index)
            {
                return new int2
                {
                    x = index % gridSize.x,
                    y = index / gridSize.x
                };

            }

            public void Execute(int index)
            {
                // Flip the flat index back to a 2d location
                var location = GetGlobalLocation(index);
                int aliveCount = 0;
                        
                for (int i = 0; i < offsetTable.Length; ++i)
                {
                    int2 entityLocation = location + offsetTable[i];
                    // wrap the cells if required
                    entityLocation.x = entityLocation.x < 0 ? gridSize.x - 1 : entityLocation.x == gridSize.x ? 0 : entityLocation.x;
                    entityLocation.y = entityLocation.y < 0 ? gridSize.y - 1 : entityLocation.y == gridSize.y ? 0 : entityLocation.y;
                                                       
                    var idx = ConvertToIndex(entityLocation, gridSize);
                    if(cellState[idx])
                    {
                        aliveCount++;
                    }
                }

                // Life condition depends on our state last time
                // If we are currently alive then we need 2 or 3 alive cells around us to stay that way
                // If we were dead then we need 3 cells to come to life
                if(cellState[index])
                {
                    newCellState[index] = (aliveCount == 2 || aliveCount == 3);
                }
                else
                {
                    newCellState[index] = aliveCount == 3;
                }
            }
        }

        public struct CellRenderingUpdate : IJobForEachWithEntity<LifeCell>
        {
            [ReadOnly] public NativeArray<bool> oldCellState;
            [ReadOnly] public NativeArray<bool> newCellState;
            [ReadOnly] public int2 GridSize;

            int ConvertToIndex(int2 location) => location.x + (location.y * GridSize.x);

            public EntityCommandBuffer.Concurrent CommandBuffer;
            public void Execute(Entity entity, int index, [ReadOnly] ref LifeCell c0)
            {
                // Because entities can move around we can't use the 'index' to look
                // up their details, instead we need to use their grid position
                // to get the correct index in to the cell state grid
                int idx = ConvertToIndex(c0.gridPosition);

                if (oldCellState[idx] != newCellState[idx])
                {
                    if(newCellState[idx])
                    {
                        CommandBuffer.AddComponent(index, entity, new AliveCell { });
                        CommandBuffer.SetComponent(index, entity, new Translation { Value = new float3(c0.gridPosition.x, 1, c0.gridPosition.y) });
                        CommandBuffer.SetSharedComponent<RenderMesh>(index, entity, LifeUpdateSystem.aliveRenderMesh);
                    }
                    else
                    {
                        CommandBuffer.RemoveComponent(index, entity, typeof(AliveCell));
                        CommandBuffer.SetComponent(index, entity, new Translation { Value = new float3(c0.gridPosition.x, 0, c0.gridPosition.y) });
                        CommandBuffer.SetSharedComponent<RenderMesh>(index, entity, LifeUpdateSystem.deadRenderMesh);
                    }
                }
            }
        }

        EntityArchetype defaultArcheType;
        BeginSimulationEntityCommandBufferSystem commandBufferSystem;

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

            // As we want the command buffer to run before any follow up systems a new one is created rather
            // than using a built in system. (Not required for version 1 but I have plans, oh yes...)
            commandBufferSystem = World.Active.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();

            // Setup the board and kick the simulation off
            GenerateLifeSeed(WorldSize);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            cellState0.Dispose();
            offsetTable.Dispose();
            cellState1.Dispose();
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
            new int2(1, 0), new int2(4, 0),
            new int2(0, 1),
            new int2(0, 2), new int2(4,2),
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
            new int2(0, 2),new int2(1, 2), new int2(4, 2),new int2(5, 2), new int2(6, 2),
        };

        int ConvertToEntityIndex(int x, int y, int2 gridSize) => x + (y * gridSize.x);
        int ConvertToEntityIndex(int2 location, int2 gridSize, int2 offset) => (location.x + offset.x) + ((location.y + offset.y) * gridSize.x);
        bool IsInValidRange(int index, int2 gridSize)
        {
            int maxValue = gridSize.x * gridSize.y;
            return index < maxValue;
        }

        // Stamp out some starting patterns to the board flipping cells from 'dead' to 'alive' as required
        void SetupBoardCondition(NativeArray<Entity> entities, NativeArray<bool> cellState, int2 gridSize, StartPatternStamp[] lifeStartPoints)
        {
            for (int startPoint = 0; startPoint < lifeStartPoints.Length; ++startPoint)
            {
                int2[] pattern = lifeStartPoints[startPoint].pattern;
                int2 offset = lifeStartPoints[startPoint].location;

                for (int idx = 0; idx < pattern.Length; ++idx)
                {
                    var location = pattern[idx];
                    int entityIndex = ConvertToEntityIndex(location, gridSize, offset);
                    if (IsInValidRange(entityIndex, gridSize))      // unlike the cells in the world for updates we aren't going to wrap here, so we just make sure we are in range
                    {
                        cellState[entityIndex] = true;
                        EntityManager.AddComponentData(entities[entityIndex], new AliveCell { });
                        // The location is adjusted slightly because it looks nicer
                        EntityManager.SetComponentData(entities[entityIndex], new Translation { Value = new float3(location.x, 1, location.y) });
                        // And we flip the mesh to the alive render mesh
                        EntityManager.SetSharedComponentData(entities[entityIndex], aliveRenderMesh);
                    }
                }
            }
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

            // Board data
            cellState0 = new NativeArray<bool>(entityCount, Allocator.Persistent);
            cellState1 = new NativeArray<bool>(entityCount, Allocator.Persistent);

            // Generate adjency information for each cell
            for (int x = 0; x < gridSize.x; ++x)
            {
                for (int y = 0; y < gridSize.y; ++y)
                {
                    int entityIdx = ConvertToEntityIndex(x, y, gridSize);
                    int2 location = new int2(x, y);

                    // Populate the entity information - all cells start off 'dead'
                    cellState0[entityIdx] = false;
                    EntityManager.SetComponentData(cells[entityIdx], new LifeCell { gridPosition = location });
                    EntityManager.SetComponentData(cells[entityIdx], new Translation { Value = new float3(x, 0, y) });
                    EntityManager.SetSharedComponentData(cells[entityIdx], deadRenderMesh);
                    /*
                    EntityElement[] adjacency = new EntityElement[8] {
                        new Entity{ Index = 1, Version = 1 } ,
                        new Entity{ Index = 1, Version = 2 } ,
                        new Entity{ Index = 1, Version = 3 } ,
                        new Entity{ Index = 1, Version = 4 } ,
                        new Entity{ Index = 1, Version = 5 } ,
                        new Entity{ Index = 1, Version = 6 },
                        new Entity{ Index = 1, Version = 7 },
                        new Entity{ Index = 1, Version = 8 }
                    };

                    DynamicBuffer<EntityElement> entityBuffer = EntityManager.GetBuffer<EntityElement>(cells[entityIdx]);
                    entityBuffer.CopyFrom(adjacency);
                    */
                }
            }

            SetupBoardCondition(cells, cellState0, gridSize, lifeStart);

            int2[] offsets = new int2[]
            {
                new int2( -1, -1 ), new int2( -0, -1 ),    new int2( 1, -1 ),
                new int2( -1,  0 ), /*new int2( -0, 0 ),*/ new int2( 1, 0 ),
                new int2( -1,  1 ), new int2( 0, 1 ),      new int2( 1, 1 ),
            };

            offsetTable = new NativeArray<int2>(offsets, Allocator.Persistent);

            cells.Dispose();
        }

        NativeArray<bool> cellState0;
        NativeArray<bool> cellState1;
        NativeArray<int2> offsetTable;

        float lastUpdateTime = 0.0f;
        bool stateSelection = true;
        int frameCount = 0;
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            if (LimitUpdateRate)
            {
                // Some update speed limiting to make the simulation look a bit nicer for humans
                lastUpdateTime -= Time.deltaTime;
                if (lastUpdateTime > 0.0f)
                    return inputDeps;

                lastUpdateTime = WorldUpdateRate;
            }

            // Two command buffers so that we can queue command from both alive and dead
            // cells updating at the same time
            var updateCommandBuffer = commandBufferSystem.CreateCommandBuffer();           

            var tileCount = WorldSize.x * WorldSize.y;

            var updateCells = new CellLifeProcessing
            {
                gridSize = WorldSize,
                cellsPerTile = CellsPerTile,
                cellState = stateSelection ? cellState0 : cellState1,
                newCellState = stateSelection ? cellState1 : cellState0,
                offsetTable = offsetTable
            }.Schedule(tileCount, 64, inputDeps);

            var renderupdate = new CellRenderingUpdate
            {
                CommandBuffer = updateCommandBuffer.ToConcurrent(),
                newCellState = stateSelection ? cellState1 : cellState0,
                oldCellState = stateSelection ? cellState0 : cellState1,
                GridSize = WorldSize
            }.Schedule(this, updateCells);

            commandBufferSystem.AddJobHandleForProducer(renderupdate);

            renderupdate.Complete();
            int changedThisFrame = 0;
            int aliveNow = 0;
            int deadNow = 0;
            for(int idx = 0; idx < tileCount; ++idx)
            {
                if (cellState0[idx] != cellState1[idx])
                {
                    changedThisFrame++;
                    if(cellState0[idx] && !cellState1[idx])
                    {
                        deadNow++;
                    }
                    else
                    {
                        aliveNow++;
                    }
                }
            }

            Debug.Log($"On frame {frameCount} - {changedThisFrame} entities flipped state : new Alive {aliveNow} : new Dead {deadNow}");
            frameCount++;
            stateSelection = !stateSelection;

            return renderupdate;
        }
    }
}
