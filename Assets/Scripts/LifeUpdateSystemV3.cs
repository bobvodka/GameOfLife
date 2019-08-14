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

namespace GameOfLifeV3
{

    struct CellState
    {
        public float timeStateChanged;
        public bool alive;
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [DisableAutoCreation]
    public class LifeUpdateSystem : JobComponentSystem
    {
        // A few config options
        private int NumberOfStartingSeeds = 12;
        private int2 worldSize = new int2 { x = 100, y = 100 };
        private uint WorldSeed = 1851936439U;
        private float WorldUpdateRate = 0.1f;
        private bool LimitUpdateRate = false;

        public float worldUpdateRate => WorldUpdateRate;

        [BurstCompile]
        public struct CellLifeProcessing : IJobParallelFor
        {
            [ReadOnly] public int2 gridSize;
            [ReadOnly] public NativeArray<bool> cellState;
            [ReadOnly] public NativeArray<int2> offsetTable;
            public NativeArray<bool> newCellState;

            int ConvertToIndex(int2 location) => location.x + (location.y * gridSize.x);

            int2 GetGlobalLocation(int index)
            {
                return new int2
                {
                    x = index % gridSize.x,
                    y = index / gridSize.x
                };

            }

            int2 WrapLocation(int2 location)
            {
                return new int2
                {
                    x = location.x < 0 ? gridSize.x - 1 : location.x == gridSize.x ? 0 : location.x,
                    y = location.y < 0 ? gridSize.y - 1 : location.y == gridSize.y ? 0 : location.y
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
                    entityLocation = WrapLocation(entityLocation);
                    var idx = ConvertToIndex(entityLocation);
                    if (cellState[idx])
                    {
                        aliveCount++;
                    }
                }

                // Life condition depends on our state last time
                // If we are currently alive then we need 2 or 3 alive cells around us to stay that way
                // If we were dead then we need 3 cells to come to life
                if (cellState[index])
                {
                    newCellState[index] = (aliveCount == 2 || aliveCount == 3);
                }
                else
                {
                    newCellState[index] = aliveCount == 3;
                }
            }
        }

        [BurstCompile]
        private struct CellStateUpdateJob : IJobParallelFor
        {
            public NativeArray<CellState> cellStates;
            [ReadOnly] public NativeArray<bool> oldCellState;
            [ReadOnly] public NativeArray<bool> newCellState;
            [ReadOnly] public float currentTime;

            public void Execute(int index)
            {
                if(oldCellState[index] == newCellState[index])
                {
                    return;
                }

                var cellState = cellStates[index];
                cellState.alive = newCellState[index];
                cellState.timeStateChanged = currentTime;
            }
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

            // Setup the board and kick the simulation off
            GenerateLifeSeed(worldSize);
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
        int ConvertToEntityIndex(int2 location, int2 gridSize) => location.x + (location.y * gridSize.x);

        // Stamp out some starting patterns to the board flipping cells from 'dead' to 'alive' as required
        void SetupBoardCondition(NativeArray<bool> cellState, int2 gridSize, StartPatternStamp[] lifeStartPoints)
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
                        cellState[entityIndex] = true;
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
            int cellCount = gridSize.x * gridSize.y;

            // Board data
            cellState0 = new NativeArray<bool>(cellCount, Allocator.Persistent);
            cellState1 = new NativeArray<bool>(cellCount, Allocator.Persistent);

            // Generate adjency information for each cell
            for (int x = 0; x < gridSize.x; ++x)
            {
                for (int y = 0; y < gridSize.y; ++y)
                {
                    int entityIdx = ConvertToEntityIndex(x, y, gridSize);

                    // Populate the entity information - all cells start off 'dead'
                    cellState0[entityIdx] = false;
                }
            }

            SetupBoardCondition(cellState0, gridSize, lifeStart);

            int2[] offsets = new int2[]
            {
                new int2( -1, -1 ), new int2( -0, -1 ),    new int2( 1, -1 ),
                new int2( -1,  0 ), /*new int2( -0, 0 ),*/ new int2( 1, 0 ),
                new int2( -1,  1 ), new int2( 0, 1 ),      new int2( 1, 1 ),
            };

            offsetTable = new NativeArray<int2>(offsets, Allocator.Persistent);
        }

        private NativeArray<bool> cellState0;
        NativeArray<bool> cellState1;
        NativeArray<int2> offsetTable;
        private NativeArray<CellState> cellStateInfo;

        float lastUpdateTime = 0.1f;
        bool stateSelection = true;

        internal NativeArray<CellState> CellStateInfo => cellStateInfo;

        public int2 WorldSize => worldSize;

        private void SwapBuffers() => stateSelection = !stateSelection;

        private NativeArray<bool> CellStateForRead => stateSelection ? cellState0 : cellState1;
        private NativeArray<bool> CellStateForWrite => stateSelection ? cellState1 : cellState0;

        private NativeArray<bool> CellStateLastFrame => CellStateForRead;
        private NativeArray<bool> CellStateCurrentFrame => CellStateForWrite;
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

            var cellCount = worldSize.x * worldSize.y;

            var updateCells = new CellLifeProcessing
            {
                gridSize = worldSize,
                cellState = CellStateForRead,
                newCellState = CellStateForWrite,
                offsetTable = offsetTable
            }.Schedule(cellCount, 64, inputDeps);

            var updateCellState = new CellStateUpdateJob
            {
                cellStates = cellStateInfo,
                newCellState = CellStateCurrentFrame,
                oldCellState = CellStateLastFrame,
                currentTime = Time.timeSinceLevelLoad
            }.Schedule(cellCount, 64, updateCells);

            SwapBuffers();
            return updateCellState;
        }
    }

    struct VertexData
    {
        public float3 position;
    }

    public class LifeRenderingSystem : JobComponentSystem
    {
        LifeUpdateSystem updateSystem;

        int3 numberCellsPerLifeCell = new int3 { x = 3, y = 3, z = 3 };
        int2 cellsPerJob = new int2 { x = 1, y = 1 };
        int2 worldSize;
        int sampledCellCount = 0;
        bool bufferSelect = false;

        // Simulation data
        int maxVertsPerCube;

        int maxVertexPerMesh = 0;
        LifeRender renderer;

        NativeArray<float> isoLevels;

        NativeArray<VertexData> vertexData0;
        NativeArray<VertexData> vertexData1;
        NativeArray<int> vertexCount0;
        NativeArray<int> vertexCount1;

        struct CellIsoLevelCalculation : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<CellState> cellStates;

            [WriteOnly]
            public NativeArray<float> isoLevels;

            [ReadOnly]
            public float currentTime;

            [ReadOnly]
            public float maxGrowthTime;

            public void Execute(int index)
            {
                CellState state = cellStates[index];
                float maxTime = maxGrowthTime + state.timeStateChanged;
                float normalisedPosition = (currentTime - state.timeStateChanged) / (maxTime - state.timeStateChanged);
                float isoLevel = state.alive ? normalisedPosition : 1 - normalisedPosition;

                isoLevels[index] = isoLevel;
            }
        }

        struct VertGenerationJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float> isoLevels;

            [WriteOnly]
            public NativeArray<int> vertexCount;

            [ReadOnly] public int2 worldSize;
            [ReadOnly] public int maxVertsPerCube;

            [ReadOnly]
            public int3 numberCellsPerLifeCell;

            [NativeDisableParallelForRestriction]
            [WriteOnly]
            public NativeArray<VertexData> vertexData;

            private int GetCubeIndex(float[] yAxisPositions, float isoLevel)
            {
                int cubeIndex = 0;
                if (yAxisPositions[0] < isoLevel) cubeIndex |= 1;
                if (yAxisPositions[1] < isoLevel) cubeIndex |= 2;
                if (yAxisPositions[2] < isoLevel) cubeIndex |= 4;
                if (yAxisPositions[3] < isoLevel) cubeIndex |= 8;
                if (yAxisPositions[4] < isoLevel) cubeIndex |= 16;
                if (yAxisPositions[5] < isoLevel) cubeIndex |= 32;
                if (yAxisPositions[6] < isoLevel) cubeIndex |= 64;
                if (yAxisPositions[7] < isoLevel) cubeIndex |= 128;

                return cubeIndex;
            }

            private float[] GenerateYAxisPositions(int x, int y, int z, int index)
            {
                float[] yValues = new float[8]
                {
                    y * 1, y * 1, y * 1, y * 1,                         // bottom row
                    (y + 1) * 1, (y + 1) * 1,(y + 1) * 1,(y + 1) * 1    // top row
                };

                return yValues;
            }

            float3 interpolateVerts(float4 v1, float4 v2, float isoLevel)
            {
                float t = (isoLevel - v1.w) / (v2.w - v1.w);
                return v1.xyz + t * (v2.xyz - v1.xyz);
            }

            // notes: need to generate a 'virtual' point inside the cube we are processing
            // for each sub-section which should be an interpolated version of the position next to us
            // so really we need another job which generates the 'now' information and then feeds in to this job
            // ok.. lets write that then...

            public void Execute(int index)
            {
                int offset = index * maxVertsPerCube;

                float isoLevel = isoLevels[index];

                // Read in the IsoLevels for the surrounding cells

                // use that to construct a local field and then work out the interpolated
                // level inside each point in our cell
                

                // Need to rework the below + support functions
                // Now we loop over the sub-cells to generate the surface
                for(int x = 0; x < numberCellsPerLifeCell.x; ++x)
                {
                    for(int y = 0; y < numberCellsPerLifeCell.y; ++y)
                    {
                        for(int z = 0; z < numberCellsPerLifeCell.z; ++z)
                        {
                            var yValues = GenerateYAxisPositions(x, y, z, index);
                            var cubeIndex = GetCubeIndex(yValues, isoLevel);

                            for (int i = 0; MarchingCubeData.triangulation[cubeIndex][i] != -1; i += 3)
                            {
                                // Get indices of corner points A and B for each of the three edges
                                // of the cube that need to be joined to form the triangle.
                                int a0 = MarchingCubeData.cornerIndexAFromEdge[MarchingCubeData.triangulation[cubeIndex][i]];
                                int b0 = MarchingCubeData.cornerIndexBFromEdge[MarchingCubeData.triangulation[cubeIndex][i]];

                                int a1 = MarchingCubeData.cornerIndexAFromEdge[MarchingCubeData.triangulation[cubeIndex][i + 1]];
                                int b1 = MarchingCubeData.cornerIndexBFromEdge[MarchingCubeData.triangulation[cubeIndex][i + 1]];

                                int a2 = MarchingCubeData.cornerIndexAFromEdge[MarchingCubeData.triangulation[cubeIndex][i + 2]];
                                int b2 = MarchingCubeData.cornerIndexBFromEdge[MarchingCubeData.triangulation[cubeIndex][i + 2]];

                                /*
                                 * todo: Generate cube corners and then rework the below to take them in to account
                                 * todo: Maybe generate the above statically and pass them in, just do a transform to worldspace before we write them out
                                Triangle tri;
                                tri.vertexA = interpolateVerts(cubeCorners[a0], cubeCorners[b0]);
                                tri.vertexB = interpolateVerts(cubeCorners[a1], cubeCorners[b1]);
                                tri.vertexC = interpolateVerts(cubeCorners[a2], cubeCorners[b2]);
                                triangles.Append(tri);
                                */
                            }
                        }
                    }
                }
            }
        }

        private void GenerateRenderingGrid()
        {
            worldSize = updateSystem.WorldSize;

            renderer = new LifeRender { mesh = new Mesh() };

            // Generate a renderer for each cluster of cells being rendered
            // Need to double buffer this as we'll be writing one frame while
            // copying the mesh data to the Mesh objects for the other
            maxVertsPerCube = 16 * numberCellsPerLifeCell.x * numberCellsPerLifeCell.y * numberCellsPerLifeCell.z;
            int maxWorldCells = worldSize.x * worldSize.y;
            int maxVertexDataCount = maxVertsPerCube * maxWorldCells;

            // Reserve enough memory for the max amount of data we could possible generate
            vertexData0 = new NativeArray<VertexData>(maxVertexDataCount, Allocator.Persistent);
            vertexData1 = new NativeArray<VertexData>(maxVertexDataCount, Allocator.Persistent);

            // Reserve enough memory for the various counts
            vertexCount0 = new NativeArray<int>(maxWorldCells, Allocator.Persistent);
            vertexCount1 = new NativeArray<int>(maxWorldCells, Allocator.Persistent);

            // And some memory for the isoLevels
            isoLevels = new NativeArray<float>(maxWorldCells, Allocator.TempJob);

        }

        public LifeRender GetLifeRender() => renderer;

        private NativeArray<VertexData> GetVertexDataForRead() => bufferSelect ? vertexData0 : vertexData1;
        private NativeArray<VertexData> GetVertexDataForWrite() => bufferSelect ? vertexData1 : vertexData0;

        private NativeArray<int> GetVertexCountPerMeshRead() => bufferSelect ? vertexCount0 : vertexCount1;
        private NativeArray<int> GetVertexCountPerMeshWrite() => bufferSelect ? vertexCount1 : vertexCount0;

        private void SwapBuffers() => bufferSelect = !bufferSelect;

        protected override void OnCreate()
        {
            base.OnCreate();

            sampledCellCount = (cellsPerJob.x * cellsPerJob.y) * 2 + 4;

            updateSystem = World.Active.GetOrCreateSystem<LifeUpdateSystem>();

            GenerateRenderingGrid();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            vertexData0.Dispose();
            vertexData1.Dispose();
            vertexCount0.Dispose();
            vertexCount1.Dispose();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            NativeArray<CellState> cellStates = updateSystem.CellStateInfo;

            // Setup job to generate the iso levels
            var isoJobHandle = new CellIsoLevelCalculation
            {
                cellStates = cellStates,
                isoLevels = isoLevels,
                currentTime = Time.timeSinceLevelLoad,
                maxGrowthTime = updateSystem.worldUpdateRate
            }.Schedule(isoLevels.Length, 64, inputDeps);


            // Setup jobs to fillout vertex data for each patch of the mesh data
            var meshGenHandle = new VertGenerationJob
            {
                isoLevels = isoLevels,
                vertexData = GetVertexDataForWrite(),
                vertexCount =  GetVertexCountPerMeshWrite(),
                worldSize = worldSize,
                maxVertsPerCube = maxVertsPerCube,
                numberCellsPerLifeCell = numberCellsPerLifeCell
            }.Schedule(isoLevels.Length, 64, isoJobHandle);
            
            // Update the rendermesh data for rendering
            // We use one giant mesh which all submeshes 
            // are built in to, this means one draw call on the backend
            var vertexData = GetVertexDataForRead();
            var vertexCount = GetVertexCountPerMeshRead();
            var renderer = GetLifeRender();
            var writeOffset = 0;
            for(int i = 0; i < vertexCount.Length; ++i)
            {
                if(vertexCount[i] > 0)
                {
                    var readOffset = maxVertexPerMesh * i;
                    renderer.mesh.SetVertexBufferData(vertexData, readOffset, writeOffset, vertexCount[i]);
                    writeOffset += vertexCount[i];
                }
            }

            SwapBuffers();
            return meshGenHandle;

        }
    }

}
