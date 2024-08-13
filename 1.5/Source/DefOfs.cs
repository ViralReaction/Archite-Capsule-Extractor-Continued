using RimWorld;
using Verse;

namespace ArchiteCapsuleExtractor
{
    [DefOf]
    public static class ArchiteCapsuleExtractor_DefOf
    {

        public static WorkGiverDef ACE_CarryToArchiteCapsuleExtractor;
        public static ThingDef ACE_ArchiteExtractor;

        static ArchiteCapsuleExtractor_DefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(ArchiteCapsuleExtractor_DefOf));

    }

}