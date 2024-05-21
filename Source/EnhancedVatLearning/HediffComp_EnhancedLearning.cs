using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using Verse;
using HarmonyLib;
using Verse.AI;
using Mono.Unix.Native;

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
    [StaticConstructorOnStartup]
    public static class HarmonyPatches
    {
        public static bool enableCompat = false;
        static HarmonyPatches()
        {
            Harmony harmony = new Harmony(id: "rimworld.smartkar.enhancedvatlearning.main");
            harmony.PatchAll();

            if (ModLister.GetActiveModWithIdentifier("com.makeitso.enhancedgrowthvatlearning") != null)
            {
                enableCompat = true;
            }
        }

        [HarmonyPatch(typeof(Hediff_VatLearning), "Learn")]
        public static class Hediff_VatLearning_Learn_Patch
        {
            public static void Postfix(Hediff_VatLearning __instance)
            {
                HediffComp_EnhancedLearning comp = __instance.TryGetComp<HediffComp_EnhancedLearning>();

                if (comp != null)
                {
                    comp.Learn();
                }
            }
        }

        [HarmonyPatch(typeof(ChoiceLetter_GrowthMoment), "ConfigureGrowthLetter")]
        public static class ChoiceLetter_GrowthMoment_ConfigureGrowthLetter_Patch
        {
            public static void Postfix(ChoiceLetter_GrowthMoment __instance)
            {
                List<HediffComp_EnhancedLearning> enhancers = __instance.pawn.health.hediffSet.hediffs.OfType<HediffWithComps>().SelectMany((HediffWithComps x) => x.comps).OfType<HediffComp_EnhancedLearning>().ToList();

                int passionsLeft = 0;

                foreach (SkillRecord record in __instance.pawn.skills.skills)
                {
                    if (record.passion != Passion.Major)
                    {
                        passionsLeft++;
                    }
                }

                foreach (HediffComp_EnhancedLearning comp in enhancers)
                {
                    __instance.traitChoiceCount += comp.additionalTraits;

                    /*
                    __instance.passionGainsCount = Math.Min(__instance.passionGainsCount + comp.additionalPassions, passionsLeft);
                    __instance.passionChoiceCount = Math.Min(__instance.passionChoiceCount + comp.additionalPassions * 2, passionsLeft);
                    comp.additionalPassions = 0;
                    comp.additionalTraits = 0;
                    passionsLeft -= comp.additionalTraits;
                    */

                    __instance.passionGainsCount = Math.Min(__instance.passionGainsCount + comp.additionalPassions, passionsLeft);
                    __instance.passionChoiceCount = Math.Min(__instance.passionChoiceCount + comp.additionalPassions * 2, passionsLeft);

                    comp.additionalPassions = 0;
                    comp.additionalTraits = 0;
                }

                __instance.CacheLetterText();
            }
        }
    }

    public class HediffComp_EnhancedLearning : HediffComp
    {
        private HediffCompProperties_EnhancedLearning Props => props as HediffCompProperties_EnhancedLearning;
        private Random rand = new Random();
        public int passionLearningCycles = 0;
        public int traitLearningCycles = 0;

        public int additionalTraits = 0;
        public int additionalPassions = 0;

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref passionLearningCycles, "passionLearningCycles");
            Scribe_Values.Look(ref traitLearningCycles, "traitLearningCycles");
            Scribe_Values.Look(ref additionalTraits, "additionalTraits");
            Scribe_Values.Look(ref additionalPassions, "additionalPassions");
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
            int linkedCognitionPods = 0;

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
                    gotVR = true;

                    foreach (Thing linked in comp.LinkedBuildings)
                    {
                        if (linked == vat || linked is not Building_GrowthVat)
                        {
                            continue;
                        }

                        Building_GrowthVat linkedVat = linked as Building_GrowthVat;

                        if (linkedVat.selectedPawn == null)
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
                }
                else if (facility.def == EVLDefOf.EVL_Cognition_Engine && !gotCognitionEngine)
                {
                    gotCognitionEngine = true;
                    additionalBoost += Props.cognitionEngineBoost;
                    CompFacility comp = facility.TryGetComp<CompFacility>();

                    foreach (Thing linked in comp.LinkedBuildings)
                    {
                        if (linked == vat || linked is not Building_GrowthVat)
                        {
                            continue;
                        }

                        Building_GrowthVat linkedVat = linked as Building_GrowthVat;

                        if (vat.selectedPawn == null)
                        {
                            continue;
                        }

                        linkedCognitionPods += 1;

                        if (linkedCognitionPods >= Props.maxCogBoost)
                        {
                            break;
                        }
                    }
                }
            }

            List<SkillRecord> skillRecords = Pawn.skills.skills.Where((SkillRecord x) => !x.TotallyDisabled).ToList();

            if (skillRecords.Count == 0)
            {
                return;
            }

            float divider = 1f;

            if (HarmonyPatches.enableCompat)
            {
                divider = 9f;
            }
            
            if (skillRecords.Where((SkillRecord x) => x.Level < 20).Any())
            {
                skillRecords.RandomElementByWeight((SkillRecord x) => x.Level >= 20 ? 0 : ((float)Math.Sqrt(x.Level) * x.LearnRateFactor(true))).Learn(additionalBoost / divider, true);
            }

            if (gotVR)
            {
                passionLearningCycles += 1;

                if (linkedVRPods <= 2)
                {
                    if (passionLearningCycles >= 3 * divider)
                    {
                        passionLearningCycles = 0;
                        additionalPassions += 1;
                    }
                }
                else if (linkedVRPods <= 5)
                {
                    if (passionLearningCycles >= 2 * divider)
                    {
                        passionLearningCycles = 0;
                        additionalPassions += 1;
                    }
                }
                else
                {
                    if (passionLearningCycles >= 1 * divider)
                    {
                        passionLearningCycles = 0;
                        additionalPassions += 1;
                    }
                }
            }

            if (gotCognitionEngine)
            {
                traitLearningCycles += 1;

                if (linkedCognitionPods <= 2)
                {
                    if (traitLearningCycles >= 2 * divider)
                    {
                        traitLearningCycles = 0;
                        additionalTraits += 1;
                    }
                }
                else
                {
                    if (traitLearningCycles >= 1 * divider)
                    {
                        traitLearningCycles = 0;
                        additionalTraits += 1;
                    }
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
        public int maxVRBoost = 8;
        public int maxCogBoost = 4;
    }
}
