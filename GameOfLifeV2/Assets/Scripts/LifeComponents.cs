﻿using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

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

    public struct WorldDetails : ISharedComponentData, IEquatable<WorldDetails>
    {
        public Entity AliveRenderer;
        public Entity DeadRenderer;
        public WorldUpdateDetails updateDetails;

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

}
