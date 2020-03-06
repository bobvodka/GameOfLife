
using Unity.Burst;

namespace GameOfLife
{
    public class GameRules
    {
        public enum RuleSet
        {
            Life,
            Replicator,
            Seeds,
            Unnamed,
            LifeWithoutDeath,
            Life34,
            Diamoeba,
            TwoByTwo,
            HighLife,
            DayAndNight,
            Morley,
            Anneal
        };

        public delegate bool LifeFunction(int aliveCount);

        static FunctionPointer<LifeFunction> CompileDelegate(LifeFunction function) => BurstCompiler.CompileFunctionPointer<LifeFunction>(function);

        static public (FunctionPointer<LifeFunction> shouldDie, FunctionPointer<LifeFunction> shouldComeToLife) GetRuleFunctions(RuleSet rules)
        {
            FunctionPointer<LifeFunction> shouldDie = default;
            FunctionPointer<LifeFunction> shouldComeToLife = default;

            switch (rules)
            {
                case RuleSet.Life:
                    shouldDie = CompileDelegate(Life.ShouldDie);
                    shouldComeToLife = CompileDelegate(Life.ComeToLife);
                    break;
                case RuleSet.Replicator:
                    shouldDie = CompileDelegate(Replicator.ShouldDie);
                    shouldComeToLife = CompileDelegate(Replicator.ComeToLife);
                    break;
                case RuleSet.Seeds:
                    shouldDie = CompileDelegate(Seeds.ShouldDie);
                    shouldComeToLife = CompileDelegate(Seeds.ComeToLife);
                    break;
                case RuleSet.Unnamed:
                    shouldDie = CompileDelegate(Unnamed.ShouldDie);
                    shouldComeToLife = CompileDelegate(Unnamed.ComeToLife);
                    break;
                case RuleSet.LifeWithoutDeath:
                    shouldDie = CompileDelegate(LifeWithoutDeath.ShouldDie);
                    shouldComeToLife = CompileDelegate(LifeWithoutDeath.ComeToLife);
                    break;
                case RuleSet.Life34:
                    shouldDie = CompileDelegate(Life34.ShouldDie);
                    shouldComeToLife = CompileDelegate(Life34.ComeToLife);
                    break;
                case RuleSet.Diamoeba:
                    shouldDie = CompileDelegate(Diamoeba.ShouldDie);
                    shouldComeToLife = CompileDelegate(Diamoeba.ComeToLife);
                    break;
                case RuleSet.TwoByTwo:
                    shouldDie = CompileDelegate(TwoByTwo.ShouldDie);
                    shouldComeToLife = CompileDelegate(TwoByTwo.ComeToLife);
                    break;
                case RuleSet.HighLife:
                    shouldDie = CompileDelegate(HighLife.ShouldDie);
                    shouldComeToLife = CompileDelegate(HighLife.ComeToLife);
                    break;
                case RuleSet.DayAndNight:
                    shouldDie = CompileDelegate(DayAndNight.ShouldDie);
                    shouldComeToLife = CompileDelegate(DayAndNight.ComeToLife);
                    break;
                case RuleSet.Morley:
                    shouldDie = CompileDelegate(Morley.ShouldDie);
                    shouldComeToLife = CompileDelegate(Morley.ComeToLife);
                    break;
                case RuleSet.Anneal:
                    shouldDie = CompileDelegate(Anneal.ShouldDie);
                    shouldComeToLife = CompileDelegate(Morley.ComeToLife);
                    break;
            }

            return (shouldDie, shouldComeToLife);
        }

        // Life functions
        [BurstCompile]
        class Life
        {
            [BurstCompile]
            static internal bool ShouldDie(int count) => !(count == 2 || count == 3);
            [BurstCompile]
            static internal bool ComeToLife(int count) => count == 3;
        }

        [BurstCompile]
        class Replicator
        {
            [BurstCompile]
            static internal bool ShouldDie(int count) => !(count == 1 || count == 3 || count == 5 || count == 7);

            [BurstCompile]
            static internal bool ComeToLife(int count) => count == 1 || count == 3 || count == 5 || count == 7;
        }

        [BurstCompile]
        class Seeds
        {
            [BurstCompile]
            static internal bool ShouldDie(int count) => true;
            [BurstCompile]
            static internal bool ComeToLife(int count) => count == 2;
        }

        [BurstCompile]
        class Unnamed
        {
            [BurstCompile]
            static internal bool ShouldDie(int count) => count != 4;
            [BurstCompile]
            static internal bool ComeToLife(int count) => count == 2 || count == 5;
        }

        [BurstCompile]
        class LifeWithoutDeath
        {
            [BurstCompile]
            static internal bool ShouldDie(int count) => false;
            [BurstCompile]
            static internal bool ComeToLife(int count) => count == 3;
        }

        [BurstCompile]
        class Life34
        {
            [BurstCompile]
            static internal bool ShouldDie(int count) => !(count == 3 || count == 4);
            [BurstCompile]
            static internal bool ComeToLife(int count) => count == 3 || count == 4;
        }

        [BurstCompile]
        class Diamoeba
        {
            [BurstCompile]
            static internal bool ShouldDie(int count) => !(count == 3 || count == 5 || count == 6 || count == 7 || count == 8);
            [BurstCompile]
            static internal bool ComeToLife(int count) => count == 5 || count == 6 || count == 7 || count == 8;
        }

        [BurstCompile]
        class TwoByTwo
        {
            [BurstCompile]
            static internal bool ShouldDie(int count) => !(count == 1 || count == 2 || count == 5);
            [BurstCompile]
            static internal bool ComeToLife(int count) => count == 3 || count == 6;
        }

        [BurstCompile]
        class HighLife
        {
            [BurstCompile]
            static internal bool ShouldDie(int count) => !(count == 2 || count == 3);
            [BurstCompile]
            static internal bool ComeToLife(int count) => count == 3 || count == 6;
        }

        [BurstCompile]
        class DayAndNight
        {
            [BurstCompile]
            static internal bool ShouldDie(int count) => !(count == 3 || count == 4 || count == 6 || count == 7 || count == 8);
            [BurstCompile]
            static internal bool ComeToLife(int count) => count == 3 || count == 6 || count == 7 || count == 8;
        }

        [BurstCompile]
        class Morley
        {
            [BurstCompile]
            static internal bool ShouldDie(int count) => !(count == 2 || count == 4 || count == 5);
            [BurstCompile]
            static internal bool ComeToLife(int count) => count == 3 || count == 6 || count == 8;
        }

        [BurstCompile]
        class Anneal
        {
            [BurstCompile]
            static internal bool ShouldDie(int count) => !(count == 3 || count == 5 || count == 6 || count == 7 || count == 8);
            [BurstCompile]
            static internal bool ComeToLife(int count) => count == 4 || count == 6 || count == 7 || count == 8;
        }

    }
}