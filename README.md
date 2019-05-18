Game Of Life
=============

Playing with various Game Of Life things in Unity using ECS.

## Details

All versions are using the Light Weight Render Pipeline.

# Version 1.0

Version 1.0 is the baseline implementation of Conway's Game Of Life.

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

# Details

This version shows the usage of a couple of things from ECS;

Usage of a '_tag component_' `AliveCell` to indicate the state of a cell in the system. This lets us filter by the tag when performing updates.

Two jobs are scheduled at once for performing the updating; this is done by having two command buffers allocated, one for each job, and the jobs themselves simply add or remove components based on the rules of the system. Everything else at this point is read only and alive/dead status is expressed via the aforementioned tag component.

The jobs themselves are tagged to either require, or exclude, the `AliveCell` component which ensures they have a unique set of data to be working from. 

The jobs look at the surrounding entities in order to update their state; this adjacency information is stored inside a data buffer as Entity `IComponentData` types can't hold arrays of data. These buffers are stored per entity instance, and in this case it stores references to `Entity`s surrounding each entity in the grid. This is safe to do as never create or destroy entities during execution. 

Related to the above is the combining of **JobHandle**s so that the command buffer system waits on both jobs before executing the updates.

Rendering is achieved via the usage of two **RenderMesh** Shared components, both of which point to a cube (via the builtin Cube Primitive type), with different materials. These are assigned to the entity when they change between dead and alive.
(The materials are the simplest shader graph shaders in existence with gpu instancing enabled.)


# Version History

1.0 - Basic implementation of Conway's Game Of Life  