using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.VFX;

namespace LifeComponents
{
    // The basics of our 'life' in the system
    // The grid position is where this cell lives
    public struct LifeCell : IComponentData
    {
        public int2 gridPosition;
    }

    // A link to our child renderable so we can we destroy it when our state changes
    public struct Renderable : IComponentData
    {
        public Entity value;
    }
    
    // As we can't store arrays of data in an IComponentData we have to use a buffer instead
    // In this case we have a buffer which holds 8 Entity references.
    // This is safe as we never really 'kill' an entity so all index data will remain stable
    // while we run.
    [InternalBufferCapacity(8)]
    public struct EntityElement : IBufferElementData
    {
        public static implicit operator Entity(EntityElement e) { return e.Value; }
        public static implicit operator EntityElement(Entity e) { return new EntityElement { Value = e }; }

        public Entity Value;
    }

    // The tag which tells us we are alive
    public struct AliveCell : IComponentData
    { }

    public struct ParticleSystemWrapper : ISharedComponentData, IEquatable<ParticleSystemWrapper>
    {
        public GameObject particleSystem;
        public Texture2D positionTexture;
        public int MaxParticles;
        public bool Equals(ParticleSystemWrapper other)
        {
            return other.particleSystem.Equals(particleSystem) 
                && other.positionTexture.Equals(positionTexture)
                && other.MaxParticles == MaxParticles;
        }

        public new int GetHashCode()
        {
            return particleSystem.GetHashCode() + positionTexture.GetHashCode() + MaxParticles;
        }
    }

    public struct WorldDetails : ISharedComponentData, IEquatable<WorldDetails>
    {
        public Entity AliveRenderer;
        public Entity DeadRenderer;
        public WorldUpdateDetails updateDetails;
        public GameObject particleSystem;
        public VisualEffect vfx;
        public Texture2D positionTexture;
        public int maxParticles;

        public bool Equals(WorldDetails other)
        {
            return AliveRenderer == other.AliveRenderer
                && DeadRenderer == other.DeadRenderer
                && updateDetails == other.updateDetails;
        }

        public new int GetHashCode()
        {
            return AliveRenderer.GetHashCode()
                + DeadRenderer.GetHashCode()
                + updateDetails.GetHashCode();
        }
    }

    public class WorldUpdateDetails
    { 
        public bool ShouldLimitUpdates;
        public float WorldUpdateRate;
        public float lastUpdateTime;
    }

    public struct SingleThreadUpdateTag : IComponentData { };
    public struct MultiThreadUpdateTag : IComponentData { };

    public struct ShouldUpdateTag : IComponentData { };
    public struct WorldUpdateTracker : IComponentData { };

    public class LifeUpdateGroup : ComponentSystemGroup { }

    public struct NewLife : IComponentData 
    {
        public Entity worldEntity;
    };
}
