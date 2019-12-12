# Game Of Life

Playing with various Game Of Life things in Unity using ECS.

## Details

All versions are using the Light Weight Render Pipeline.

## Game Of Life Details

Uses the rule set;

- 2 or 3 surrounding alive cells keep a cell alive
- more or less than that causes an alive cell to die
- if a cell has 3 surrounding cells that are alive then it becomes alive

Grid size and starting pattern stamps are currently only configurable in code (see lines 39 to 42).

Included starting patterns are;

- Glider
- Light Weight Space Ship
- Pentomino
- Acorn

## Version 2.0

Refinement of Version 1.0.

### Details

This version changes from a pure ECS system to a mix of Job System directly and ECS jobs.

Entities are still used to reflect the rendering state in the world as in Version 1, however they no longer know directly if they are alive or dead.

Instead that data is stored in a pair of `NativeArray<bool>` arrays which maintain the grid state. An `IJobParallelFor` job is then used to iterate over the current frame's cell data and write out if the cells in question are alive or dead, as per the rules of the game. Unlike the previous version where the neighbouring entities were calculated directly and stored in a lookup table, this version calculates the offsets on the fly; this is required because all we have is an index into the data rather than the [x, y] pair stored in the previous version. However as we are only touching primitive data (`bool`s in this case), this does come with the added advantage that `[BurstCompile]` can be defined for the function which improves the execution speed.

The dead/alive rendering flip is performed in a standard ECS `IJobForEachWithEntity` job; this converts the entity's [x, y] information into a an array index and queries the old and new state, if this state has changed then a command buffer is used to record if we should now be alive or dead as required.

Rendering is the same as version 1.0, a simple cube which is referenced by two `RenderMesh` shared components and flipped on alive/dead status update.

### Performance Changes

When compared to Version 1.0, even without enabling `[BurstCompile]` on the update function there is a significant change in performance. 

The first big change comes in _Simulation Group Update_, which the group our system lives in.

In Version 1.0 this clocks in at **14.80ms** with a very large chunk of that time being allocated to the `EntityCommandBuffer.Playback` function, which takes approximately 7ms per command buffer, accounting for most of that update time.
By contrast Version 2.0 clocks in at **3.66ms** with, once again, the `EntityCommandBuffer.Playback` function taking up most of that time but only 3.45ms for the single command buffer, approximately 1/4 the time of the Version 1.0 total time.
(Something about that feels off as the only difference between the two systems is that the Version 1.0 adds or removes a _tag component_ where as the Version 2.0 system doesn't so this change in playback time seems excessive.)

When it comes to the various jobs themselves the speed difference is once again significant.

In Version 1.0 our two jobs take the following amount of time per batch in the profiler;

- AliveCellProcessorJob : ~0.5ms
- DeadCellProcessorJob : ~3.0ms

By Contrast Version 2.0 has the following performance numbers;

- CellLifeProcessing : ~1.0ms
- CellLifeProcessing Burst Compiled : 0.04ms
- CellRenderingUpdate : 0.3ms

A night and day difference in the default case, and a gap which only gets significantly wider when `[BurstCompile]` is enabled for the _CellLifeProcessing_ job, something that can't be done for either job in the Version 1.0 system.

## Version 1.0

Version 1.0 is the baseline implementation of Conway's Game Of Life.

### Details

This version shows the usage of a couple of things from ECS;

Usage of a '_tag component_' `AliveCell` to indicate the state of a cell in the system. This lets us filter by the tag when performing updates.

Two jobs are scheduled at once for performing the updating; this is done by having two command buffers allocated, one for each job, and the jobs themselves simply add or remove components based on the rules of the system. Everything else at this point is read only and alive/dead status is expressed via the aforementioned tag component.

The jobs themselves are tagged to either require, or exclude, the `AliveCell` component which ensures they have a unique set of data to be working from.

The jobs look at the surrounding entities in order to update their state; this adjacency information is stored inside a data buffer as Entity `IComponentData` types can't hold arrays of data. These buffers are stored per entity instance, and in this case it stores references to `Entity`s surrounding each entity in the grid. This is safe to do as never create or destroy entities during execution.

Related to the above is the combining of `JobHandle`s so that the command buffer system waits on both jobs before executing the updates.

Rendering is achieved via the usage of two `RenderMesh` Shared components, both of which point to a cube (via the builtin Cube Primitive type), with different materials. These are assigned to the entity when they change between dead and alive.
(The materials are the simplest shader graph shaders in existence with gpu instancing enabled.)

## Version History

2.0 - Updated to use a more basic loop and only use the entities to visualise the data in the world.
1.0 - Basic implementation of Conway's Game Of Life.  