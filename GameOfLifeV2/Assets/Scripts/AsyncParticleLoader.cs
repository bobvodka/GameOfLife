using LifeComponents;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.VFX;

[AlwaysSynchronizeSystem]
public class AsyncParticleLoader : SystemBase
{
    EndSimulationEntityCommandBufferSystem cmdBufferSystem;

    protected override void OnCreate()
    {
        cmdBufferSystem = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
    }

    protected override void OnUpdate()
    {

        var cmds = cmdBufferSystem.CreateCommandBuffer();

        Entities
            .WithoutBurst()
            .ForEach((Entity entity, AsyncRequestWorld request) =>
        {
            if(request.particleGO.IsDone)
            {
                var particleSystem = request.particleGO.Result;

                var vfxSystem = particleSystem.GetComponent<VisualEffect>();

                // Generate the texture required to sort the position data in it
                var positionData = new Texture2D(request.MaxParticles, 1, TextureFormat.RGFloat, false);
                vfxSystem.SetTexture("particlePositions", positionData);

                // Setup some extents so that the system will simulate/render correctly
                vfxSystem.SetVector3("Centre", request.CentrePoint);
                var extents = new float3(request.GridSize.x / 2.0f, 5.0f, request.GridSize.y / 2.0f);
                vfxSystem.SetVector3("Extent", extents);

                request.worldParticleDetails.particleSystem = particleSystem;
                request.worldParticleDetails.positionTexture = positionData;
                request.worldParticleDetails.vfx = vfxSystem;

                cmds.DestroyEntity(entity);
            }
        }).Run();

    }
}