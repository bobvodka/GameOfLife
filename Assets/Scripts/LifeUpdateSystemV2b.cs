using UnityEngine;
using UnityEditor;

using Unity.Jobs;
using Unity.Entities;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Rendering;
using Unity.Transforms;

using LifeComponents;

namespace GameOfLifeV2b
{

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    //[DisableAutoCreation]
    public class LifeUpdateSystem : JobComponentSystem
    {
        // A few config options
        private int NumberOfStartingSeeds = 12;
        private static int2 WorldSize = new int2 { x = 100, y = 100 };
        private static int bitsPerCells = 32;
        private static int2 GridSize = new int2 { x = (int)math.ceil((float)WorldSize.x / (float)bitsPerCells), y = WorldSize.y}; 
        private uint WorldSeed = 1851936439U;
        private float WorldUpdateRate = 0.1f;
        private bool LimitUpdateRate = false;

        //[BurstCompile]
        public struct CellLifeProcessing : IJobParallelFor
        {
            [ReadOnly] public NativeArray<uint> cellState;
            [ReadOnly] public int2 gridSize;
            [ReadOnly] public int2 gridSizeInTiles;
            [ReadOnly] public int cellsPerTile;

            public NativeArray<uint> newCellState;

            int ConvertToTileIndex(int2 location) => (location.x / cellsPerTile) + (location.y * gridSizeInTiles.x);

            // Location inside the 2d tiled grid
            int2 GetGlobalLocation(int index)
            {

                return new int2
                {
                    x = index / cellsPerTile,
                    y = index / gridSize.x
                };

            }

            int2 WrapLocation(int2 location)
            {
                return new int2
                {
                    x = location.x < 0 ? gridSizeInTiles.x - 1 : location.x == gridSizeInTiles.x ? 0 : location.x,
                    y = location.y < 0 ? gridSizeInTiles.y - 1 : location.y == gridSizeInTiles.y ? 0 : location.y
                };
            }

            int GetValidBitCount(int2 location)
            {
                return math.min(cellsPerTile, (gridSize.x - (location.x * cellsPerTile)));
            }

            uint LoadLineAtOffset(int2 location, int2 offset)
            {
                var lineLocation = WrapLocation(location + offset);
                var idx = ConvertToTileIndex(lineLocation);

                return cellState[idx];
            }

            void CellOutUpdate(ref uint rowOut, uint cells, int idx, int aliveCount)
            {
                if ((cells & (1 << idx)) > 0)
                {
                    if (aliveCount == 2 || aliveCount == 3)
                    {
                        rowOut |= 1u << idx;
                    }
                }
                else
                {
                    if (aliveCount == 3)
                    {
                        rowOut |= 1u << idx;
                    }
                }
            }

            // Process a row from 1 -> validBits count
            public void ProcessRow(ref uint rowOut, int validBits, uint cells, uint cellsAbove, uint cellsBelow)
            {
                uint adjMask = 0x5;
                uint rowMask = 0x7;

                for (int idx = 1; idx < validBits - 1; idx++)
                {
                    var rowAlive = cells & adjMask;
                    var rowAboveAlive = cellsAbove & rowMask;
                    var rowBelowAlive = cellsBelow & rowMask;

                    var aliveCount = math.countbits(rowAlive) + math.countbits(rowAboveAlive) 
                        + math.countbits(rowBelowAlive);

                    CellOutUpdate(ref rowOut, cells, idx, aliveCount);
                }
            }

            void ProcessEdge(ref uint rowOut, int2 location, uint cells, uint cellsAbove, uint cellsBelow, uint edgeMask, uint rowMask, uint adjMask, int2[] locations)
            {
                uint edge = 0;

                // First we deal with the rows which we don't have yet
                // loading each in turn and masking off all but the bits we care about
                for (int idx = 0; idx < 3; idx++)
                {
                    var leftCells = LoadLineAtOffset(location, locations[idx]) & edgeMask;
                    edge |= leftCells << idx;
                }

                var aliveCount = math.countbits(edge);
                // we can do an early check here, as we know from the rules that greater than
                // 3 alive cells means this cell must be dead next frame
                if (aliveCount > 3)
                    return;

                // Otherwise grab the remaining cells and complete the count
                aliveCount += math.countbits(cellsAbove & rowMask) + math.countbits(cellsBelow & rowMask);
                aliveCount += math.countbits(cells & adjMask);

                CellOutUpdate(ref rowOut, cells, 0, aliveCount);
            }

            void ProcessLeftEdge(ref uint rowOut, int2 location, uint cells, uint cellsAbove, uint cellsBelow)
            {

                // We need to load the left edge values and then only mask the high bit for each one
                var edgeMask = 0x80000000u;

                int2[] locations = new int2[]
                {
                    new int2 { x = -1, y =  1 },
                    new int2 { x = -1, y =  0 },
                    new int2 { x = -1, y = -1 }
                };

                // These ensure we only load the low bits of the rows above and below us, as well as the cell to the right
                var rowMask = 0x3u;
                var adjMask = 0x2u;

                ProcessEdge(ref rowOut, location, cells, cellsAbove, cellsBelow, edgeMask, rowMask, adjMask, locations);

            }

            void ProcessRightEdge(ref uint rowOut, int2 location, uint cells, uint cellsAbove, uint cellsBelow)
            {
                // We need to load the right edge values and then only mask the high bit for each one
                var edgeMask = 0x1u;

                int2[] locations = new int2[]
                {
                    new int2 { x = 1, y =  1 },
                    new int2 { x = 1, y =  0 },
                    new int2 { x = 1, y = -1 }
                };

                // These ensure we only load the high bits for the rows above and below us and the cell to the left
                var rowMask = 0xC0000000u;
                var adjMask = 0x40000000u;
                ProcessEdge(ref rowOut, location, cells, cellsAbove, cellsBelow, edgeMask, rowMask, adjMask, locations);
            }

            public void Execute(int index)
            {
                // Our index is an index in to a tiled array
                // so we can directly load that
                var cells = cellState[index];

                // Next we need to load the lines around us
                // For that we need our 2d tile location
                var location = GetGlobalLocation(index);

                // Then we can load the easy values (above and below)
                var cellsAbove = LoadLineAtOffset(location, new int2 { x = 0, y = -1 });
                var cellsBelow = LoadLineAtOffset(location, new int2 { x = 0, y = 1  });


                uint rowOut = 0;
                var validBitCount = GetValidBitCount(location);

                // Process the easy cases first (cells 1 to validBitCount - 1)
                ProcessRow(ref rowOut, validBitCount, cells, cellsAbove, cellsBelow);
                // Next we have to handle the (literal) edge cases
                ProcessLeftEdge(ref rowOut, location, cells, cellsAbove, cellsBelow);
                ProcessRightEdge(ref rowOut, location, cells, cellsAbove, cellsBelow);

                // Finally write out the new cell data
                newCellState[index] = rowOut;

            }
        }

        public struct CellRenderingUpdate : IJobForEachWithEntity<LifeCell>
        {
            [ReadOnly] public NativeArray<uint> oldCellState;
            [ReadOnly] public NativeArray<uint> newCellState;
            [ReadOnly] public int2 gridSizeInTiles;
            [ReadOnly] public int cellsPerTile;

            int ConvertToIndex(int2 location)
            {
                var x = location.x % cellsPerTile;
                return gridSizeInTiles.x * location.y + x;
            }

            int ConvertToTileIndex(int2 location) => (location.x / cellsPerTile) + (location.y * gridSizeInTiles.x);

            int GetBitForCell(int2 location)
            {
                return location.x % cellsPerTile;
            }

            public EntityCommandBuffer.Concurrent CommandBuffer;
            public void Execute(Entity entity, int index, [ReadOnly] ref LifeCell c0)
            {
                // Because entities can move around in chunks we can't use the 'index' to look
                // up their details, instead we need to use their grid position
                // to get the correct index in to the correct bit which represents the cell state 
                // in the grid

                int idx = ConvertToTileIndex(c0.gridPosition);

                // Load in the cells
                var oldState = oldCellState[idx];
                var newState = newCellState[idx];

                var mask = GetBitForCell(c0.gridPosition);

                if ((oldState & mask) != (newState & mask))
                {
                    if ((newState & mask) > 0)
                    {
                        CommandBuffer.SetComponent(index, entity, new Translation { Value = new float3(c0.gridPosition.x, 1, c0.gridPosition.y) });
                        CommandBuffer.SetSharedComponent<RenderMesh>(index, entity, LifeUpdateSystem.aliveRenderMesh);
                    }
                    else
                    {
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
                typeof(Translation),
                typeof(RenderMesh),
                typeof(LocalToWorld)
                );

            // Grab a built in command buffer so that we can queue up updates for execution on the main thread
            // at some point in the future (in this case, before the next time 'update' runs
            commandBufferSystem = World.Active.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();

            // Setup the board and kick the simulation off
            GenerateLifeSeed(WorldSize);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            cellState0.Dispose();
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

        int ConvertToIndex(int2 location) => location.x + (location.y * GridSize.x);

        int ConvertToTileIndex(int2 location) => (location.x / bitsPerCells) + (location.y * GridSize.x);

        void SetAliveAtLocation(NativeArray<uint> cells, int2 location, int2 offset)
        {
            var lineLocation = location + offset;
            var idx = ConvertToTileIndex(lineLocation);

            var cellState = cells[idx];

            int bitIdx = location.x % bitsPerCells;
            cellState |= 1u << bitIdx;

            cells[idx] = cellState;
        }

        // Stamp out some starting patterns to the board flipping cells from 'dead' to 'alive' as required
        void SetupBoardCondition(NativeArray<Entity> entities, NativeArray<uint> cellState, int2 gridSize, StartPatternStamp[] lifeStartPoints)
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
                        SetAliveAtLocation(cellState, location, offset);
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
            cellState0 = new NativeArray<uint>(entityCount, Allocator.Persistent);
            cellState1 = new NativeArray<uint>(entityCount, Allocator.Persistent);

            // Generate adjency information for each cell
            for (int x = 0; x < gridSize.x; ++x)
            {
                for (int y = 0; y < gridSize.y; ++y)
                {
                    int entityIdx = ConvertToEntityIndex(x, y, gridSize);

                    // Populate the entity information - all cells start off 'dead'
                    cellState0[entityIdx] = 0;
                    EntityManager.SetComponentData(cells[entityIdx], new LifeCell { gridPosition = new int2(x, y) });
                    EntityManager.SetComponentData(cells[entityIdx], new Translation { Value = new float3(x, 0, y) });
                    EntityManager.SetSharedComponentData(cells[entityIdx], deadRenderMesh);
                }
            }

            SetupBoardCondition(cells, cellState0, gridSize, lifeStart);


            cells.Dispose();
        }

        NativeArray<uint> cellState0;
        NativeArray<uint> cellState1;

        float lastUpdateTime = 0.0f;
        bool stateSelection = true;
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

            // We need a command buffer to update the entities from inside a job
            var updateCommandBuffer = commandBufferSystem.CreateCommandBuffer();

            var tileCount = GridSize.x * GridSize.y;

            var updateCells = new CellLifeProcessing
            {
                gridSize = WorldSize,
                gridSizeInTiles = GridSize,
                cellState = stateSelection ? cellState0 : cellState1,
                newCellState = stateSelection ? cellState1 : cellState0,
                cellsPerTile = bitsPerCells
            }.Schedule(tileCount, 1, inputDeps);

            var renderupdate = new CellRenderingUpdate
            {
                CommandBuffer = updateCommandBuffer.ToConcurrent(),
                newCellState = stateSelection ? cellState1 : cellState0,
                oldCellState = stateSelection ? cellState0 : cellState1,
                cellsPerTile = bitsPerCells
            }.Schedule(this, updateCells);

            commandBufferSystem.AddJobHandleForProducer(renderupdate);
            stateSelection = !stateSelection;

            return renderupdate;
        }
    }
}
