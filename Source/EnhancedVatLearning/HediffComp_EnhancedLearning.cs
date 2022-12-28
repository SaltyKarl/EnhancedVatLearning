using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using Verse;

[DefOf]
public static class EVLDefOf
{
    static EVLDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(EVLDefOf));
    }

    public static ThingDef EVL_Neurostimulator;

    public static ThingDef EVL_VR_Simulator;

    public static ThingDef EVL_Cognition_Engine;
}

namespace EnhancedVatLearning
{

    public class HediffComp_EnhancedLearning : HediffComp
    {
        private HediffCompProperties_EnhancedLearning Props => props as HediffCompProperties_EnhancedLearning;
        private Random rand = new Random();
        public int passionLearningCycles = 0;
        public int traitLearningCycles = 0;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            if (parent.Severity >= parent.def.maxSeverity)
            {
                Learn();
            }
        }

        public int GetRandomIndex(List<double> weights)
        {
            double curSum = 0;
            double randomNum = rand.NextDouble() * weights.Sum();
            for (int i = 0; i < weights.Count; i++)
            {
                if (weights[i] == 0)
                {
                    continue;
                }

                curSum += weights[i];

                if (randomNum <= curSum)
                {
                    return i;
                }
            }

            return 0; //somehow
        }

        public void Learn()
        {
            if (Pawn.skills == null)
            {
                return;
            }

            if (Pawn.ParentHolder == null || Pawn.ParentHolder is not Building_GrowthVat)
            {
                return;
            }

            float additionalBoost = 0;

            Building_GrowthVat vat = Pawn.ParentHolder as Building_GrowthVat;
            CompAffectedByFacilities facilityComp = vat.TryGetComp<CompAffectedByFacilities>();

            bool gotNeurostim = false;
            bool gotVR = false;
            bool gotCognitionEngine = false;
            int linkedVRPods = 0;

            foreach (Thing facility in facilityComp.LinkedFacilitiesListForReading)
            {
                if (facility.def == EVLDefOf.EVL_Neurostimulator && !gotNeurostim)
                {
                    additionalBoost += Props.neurostimBoost;
                    gotNeurostim = true;
                }
                else if (facility.def == EVLDefOf.EVL_VR_Simulator && !gotVR)
                {
                    CompFacility comp = facility.TryGetComp<CompFacility>();
                    additionalBoost += Props.vrBoost;

                    foreach (Thing linked in comp.LinkedBuildings)
                    {
                        if (linked == vat || linked is not Building_GrowthVat)
                        {
                            continue;
                        }

                        if (typeof(Building_GrowthVat).GetField("selectedPawn", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(linked) == null)
                        {
                            continue;
                        }

                        additionalBoost += Props.vrBoostAdditional;
                        linkedVRPods += 1;

                        if (linkedVRPods >= Props.maxVRBoost)
                        {
                            break;
                        }
                    }

                    gotVR = true;
                }
                else if (facility.def == EVLDefOf.EVL_Cognition_Engine && !gotCognitionEngine)
                {
                    gotCognitionEngine = true;
                    additionalBoost += Props.cognitionEngineBoost;
                }
            }

            List<SkillRecord> skillRecords = Pawn.skills.skills.Where((SkillRecord x) => !x.TotallyDisabled).ToList();

            if (skillRecords.Count == 0)
            {
                return;
            }

            List<double> skillWeights = new List<double>();
            List<double> passionWeights = new List<double>();

            foreach (SkillRecord record in skillRecords)
            {
                skillWeights.Add(Math.Sqrt(record.Level) * record.LearnRateFactor(true) * (record.Level >= 20 ? 0 : 1));
                passionWeights.Add(2 - (int)record.passion);
            }

            skillRecords[GetRandomIndex(skillWeights)].Learn(additionalBoost, true);

            if (gotVR)
            {
                passionLearningCycles += 1;

                if (passionLearningCycles > (Props.cycleReq / Math.Round(Math.Sqrt(linkedVRPods), 0, MidpointRounding.AwayFromZero)))
                {
                    passionLearningCycles = 0;
                    skillRecords[GetRandomIndex(passionWeights)].passion += 1;
                }
            }

            if (gotCognitionEngine)
            {
                traitLearningCycles += 1;
                if (traitLearningCycles >= rand.Next(6, 9))
                {
                    traitLearningCycles = 0;
                    Pawn.story.traits.GainTrait(PawnGenerator.GenerateTraitsFor(Pawn, 1, null, true)[0]);
                }
            }
        }
    }

    public class HediffCompProperties_EnhancedLearning : HediffCompProperties
    {
        public HediffCompProperties_EnhancedLearning()
        {
            this.compClass = typeof(HediffComp_EnhancedLearning);
        }

        public float neurostimBoost = 4000;
        public float vrBoost = 1200;
        public float vrBoostAdditional = 1200;
        public float cognitionEngineBoost = 2000;
        public int maxVRBoost = 6;
        public int cycleReq = 6;
    }
}
