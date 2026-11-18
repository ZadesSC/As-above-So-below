using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace AsAboveSoBelow
{
    /// <summary>
    /// V2 stairwell travel orders.
    ///
    /// Movement between bands is automatic once a destination in another band is chosen -
    /// Pawn_PathFollower.StartPath segments the trip. The gap is that the PLAYER has no way
    /// to express such a destination: only one band is visible at a time, so a
    /// right-click destination is always in the current band, and clicking the stairwell
    /// itself just walks the pawn onto that cell and stops.
    ///
    /// This provider closes that gap by offering the far end as an explicit destination. It
    /// serves DRAFTED pawns as well as undrafted, since ordering soldiers up or down is
    /// exactly when it matters most.
    /// </summary>
    public class FloatMenuOptionProvider_ABStairsTravel : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;

        protected override bool Undrafted => true;

        protected override bool Multiselect => true;

        /// <summary>
        /// One option PER far end, not one option total.
        ///
        /// The original override was GetSingleOptionFor, which structurally cannot offer
        /// two directions - and the elevator has two or more ends. Its "best counterpart"
        /// fallback returned the FIRST end, and TryEstablish adds ends bottom-up, so a
        /// surface elevator's first end is always the BASEMENT: the float menu offered
        /// "go down" and nothing else. Reported as "the elevator only works going down" -
        /// the up transit was fine; the player was never given a way to order it.
        /// </summary>
        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing clickedThing, FloatMenuContext context)
        {
            List<Pawn> list = new List<Pawn>();
            foreach (Pawn validSelectedPawn in context.ValidSelectedPawns)
            {
                if (validSelectedPawn != null)
                {
                    list.Add(validSelectedPawn);
                }
            }

            return TravelOptionsFor(list, clickedThing as Building_ABStairs2);
        }

        /// <summary>
        /// The travel options for one pawn and one link, shared verbatim between the
        /// float-menu provider above and the Perspective Shift bridge (a hauling avatar's
        /// left-click on a link travels instead of depositing - window 15). Extracted so the
        /// two entry points cannot drift: same labels, same reachability rule, same
        /// disembark-tile destination. Null-action "(no path)" rows are included, exactly as
        /// the provider always served them; callers that need only executable orders filter
        /// on <c>action != null</c>.
        /// </summary>
        internal static List<FloatMenuOption> TravelOptionsFor(Pawn pawn, Building_ABStairs2 stairs)
        {
            List<Pawn> list = new List<Pawn>(1);
            if (pawn != null)
            {
                list.Add(pawn);
            }

            return TravelOptionsFor(list, stairs);
        }
        internal static List<FloatMenuOption> TravelOptionsFor(List<Pawn> pawns, Building_ABStairs2 stairs)
        {
            List<FloatMenuOption> list = new List<FloatMenuOption>();
            try
            {
                if (!ABGuard.On(ABGuard.Movement))
                {
                    return list;
                }

                if (stairs == null || !stairs.Spawned || pawns == null || pawns.Count == 0)
                {
                    return list;
                }

                List<Pawn> movers = new List<Pawn>(pawns.Count);
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];
                    if (pawn != null && pawn.Spawned && pawn.Map == stairs.Map && pawn.CanReach(stairs, PathEndMode.OnCell, Danger.Deadly))
                    {
                        movers.Add(pawn);
                    }
                }

                Pawn pawn2 = ((movers.Count > 0) ? movers[0] : pawns[0]);
                if (pawn2 == null || pawn2.Map != stairs.Map)
                {
                    return list;
                }

                ABBandMap aBBandMap = ABBands.CompOf(stairs.Map);
                if (aBBandMap == null || !aBBandMap.Banded)
                {
                    return list;
                }

                bool flag = movers.Count > 0;
                int num = aBBandMap.BandOf(stairs.Position);
                bool flag2 = stairs.Counterparts.Count > 1;
                for (int j = 0; j < stairs.Counterparts.Count; j++)
                {
                    Building_ABStairs2 building_ABStairs = stairs.Counterparts[j];
                    if (building_ABStairs == null || !building_ABStairs.Spawned)
                    {
                        continue;
                    }

                    int num2 = aBBandMap.BandOf(building_ABStairs.Position) - num;
                    string key;
                    if (stairs.LinksAllLevels)
                    {
                        key = ((num2 > 0) ? "AB_GoUpElevator" : "AB_GoDownElevator");
                    }
                    else if (stairs.def.defName.IndexOf("Ladder", StringComparison.Ordinal) < 0)
                    {
                        key = ((num2 > 0) ? "AB_GoUp" : "AB_GoDown");
                    }
                    else
                    {
                        key = ((num2 > 0) ? "AB_GoUpLadder" : "AB_GoDownLadder");
                    }

                    string text = key.Translate();
                    if (flag2)
                    {
                        text = "AB_GoToLevel".Translate(text, ABBands.LevelOf(stairs.Map, building_ABStairs.Position));
                    }

                    if (!flag)
                    {
                        list.Add(new FloatMenuOption(text + " (" + "NoPath".Translate() + ")", null));
                        continue;
                    }

                    IntVec3 dest = ABLinkApproach.DisembarkCell(building_ABStairs);
                    if (!dest.IsValid)
                    {
                        dest = building_ABStairs.Position;
                    }

                    list.Add(FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(text, () =>
                    {
                        for (int k = 0; k < movers.Count; k++)
                        {
                            Pawn pawn3 = movers[k];
                            if (pawn3 != null && pawn3.Spawned && pawn3.jobs != null)
                            {
                                Job job = JobMaker.MakeJob(JobDefOf.Goto, dest);
                                job.locomotionUrgency = LocomotionUrgency.Jog;
                                pawn3.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                            }
                        }
                    }), pawn2, dest));
                }
            }
            catch (Exception e)
            {
                ABGuard.Disable(ABGuard.Movement, e, "V2 stairs float menu");
            }

            return list;
        }

    }
}
