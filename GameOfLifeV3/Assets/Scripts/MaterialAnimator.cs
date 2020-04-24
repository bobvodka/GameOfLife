using LifeComponents;
using LifeUpdateSystem;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace GameOfLife
{
    struct MaterialAnimationInfo : IComponentData
    {
        public float4 startColour;
        public float4 endColour;
        public float startPosition;
        public float endPosition;
        public float endTime;
        public float totalTime;
    }

    [MaterialProperty("_Colour", MaterialPropertyFormat.Float4)]
    struct ColourOverride : IComponentData
    {
        public float4 Value;
    }

    [MaterialProperty("YOffset", MaterialPropertyFormat.Float)]
    struct YOffsetOveride : IComponentData
    {
        public float Value;
    }

    [AlwaysSynchronizeSystem]
    [UpdateInGroup(typeof(LifeUpdateGroup))]
    [UpdateBefore(typeof(LifeUpdateAnimated))]
    public class MaterialAnimator : SystemBase
    {
        protected override void OnUpdate()
        {
            var now = Time.ElapsedTime;
            Entities.ForEach((ref YOffsetOveride yOffset, ref ColourOverride colour, in MaterialAnimationInfo info) =>
            {
                var t = (float)(1.0f - ((info.endTime - now) / info.totalTime));

                var lowest = math.min(info.startPosition, info.endPosition);
                var highest = math.max(info.startPosition, info.endPosition);

                var lowestColour = math.min(info.startColour, info.endColour);
                var highestColour = math.max(info.startColour, info.endColour);
                                
                yOffset.Value = math.clamp(math.lerp(info.startPosition, info.endPosition, t), lowest, highest);
                colour.Value = math.clamp(math.lerp(info.startColour, info.endColour, t), lowestColour, highestColour);

            }).ScheduleParallel();
        }
    }
}