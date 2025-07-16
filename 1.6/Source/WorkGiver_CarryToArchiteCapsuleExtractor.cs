using Verse;
using RimWorld;

namespace ArchiteCapsuleExtractor
{
    public class WorkGiver_CarryToArchiteCapsuleExtractor : WorkGiver_CarryToBuilding
    {

        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(ArchiteCapsuleExtractor_DefOf.ACE_ArchiteExtractor);

        public override bool ShouldSkip(Pawn pawn, bool forced = false) => !ModsConfig.BiotechActive;
    }
}
