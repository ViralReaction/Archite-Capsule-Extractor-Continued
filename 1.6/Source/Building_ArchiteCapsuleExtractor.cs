using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using RimWorld;

namespace ArchiteCapsuleExtractor
{

	[StaticConstructorOnStartup]
	public class Building_ArchiteCapsuleExtractor : Building_Enterable, IThingHolderWithDrawnPawn, IThingHolder
	{
		private int ticksRemaining;

		private int powerCutTicks;

		[Unsaved(false)]
		private CompPowerTrader cachedPowerComp;

		[Unsaved(false)]
		private Texture2D cachedInsertPawnTex;

		[Unsaved(false)]
		private Sustainer sustainerWorking;

		[Unsaved(false)]
		private Effecter progressBar;

		private const int TicksToExtract = 30000;

		private const int NoPowerEjectCumulativeTicks = 60000;

		private static readonly Texture2D CancelIcon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel");

		private Pawn ContainedPawn
		{
			get
			{
				if (innerContainer.Count <= 0) return null;
				return (Pawn)innerContainer[0];
			}
		}

		private bool PowerOn => PowerTraderComp.PowerOn;

		public override bool IsContentsSuspended => false;

		private CompPowerTrader PowerTraderComp => cachedPowerComp ?? (cachedPowerComp = this.TryGetComp<CompPowerTrader>());

		private Texture2D InsertPawnTex
		{
			get
			{
				if (cachedInsertPawnTex == null)
				{
					cachedInsertPawnTex = ContentFinder<Texture2D>.Get("UI/Gizmos/InsertPawn");
				}
				return cachedInsertPawnTex;
			}
		}

		public float HeldPawnDrawPos_Y => DrawPos.y + 3f / 74f;

		public float HeldPawnBodyAngle => Rotation.Opposite.AsAngle;

		public PawnPosture HeldPawnPosture => PawnPosture.LayingOnGroundFaceUp;

		public override Vector3 PawnDrawOffset => IntVec3.West.RotatedBy(Rotation).ToVector3() / def.size.x;

		public override void PostPostMake()
		{
			if (!ModLister.CheckBiotech("gene extractor"))
			{
				Destroy();
			}
			else
			{
				base.PostPostMake();
			}
		}

		public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
		{
			sustainerWorking = null;
			if (progressBar != null)
			{
				progressBar.Cleanup();
				progressBar = null;
			}
			base.DeSpawn(mode);
		}

		protected override void Tick()
		{
			base.Tick();
			if (this.IsHashIntervalTick(250))
			{
				PowerTraderComp.PowerOutput = (Working ? (0f - PowerComp.Props.PowerConsumption) : (0f - PowerComp.Props.idlePowerDraw));
			}
			if (Working)
			{
				if (ContainedPawn == null)
				{
					Cancel();
					return;
				}
				if (PowerTraderComp.PowerOn)
				{
					TickEffects();
					if (PowerOn)
					{
						ticksRemaining--;
					}
					if (ticksRemaining <= 0)
					{
						Finish();
					}
					return;
				}
				powerCutTicks++;
				if (powerCutTicks >= NoPowerEjectCumulativeTicks)
				{
					Pawn containedPawn = ContainedPawn;
					if (containedPawn != null)
					{
						Messages.Message("GeneExtractorNoPowerEjectedMessage".Translate(containedPawn.Named("PAWN")), containedPawn, MessageTypeDefOf.NegativeEvent, historical: false);
					}
					Cancel();
				}
			}
			else
			{
				if (selectedPawn != null && selectedPawn.Dead)
				{
					Cancel();
				}
				if (progressBar != null)
				{
					progressBar.Cleanup();
					progressBar = null;
				}
			}
		}

		private void TickEffects()
		{
			if (sustainerWorking == null || sustainerWorking.Ended)
			{
				sustainerWorking = SoundDefOf.GeneExtractor_Working.TrySpawnSustainer(SoundInfo.InMap(this, MaintenanceType.PerTick));
			}
			else
			{
				sustainerWorking.Maintain();
			}
			if (progressBar == null)
			{
				progressBar = EffecterDefOf.ProgressBarAlwaysVisible.Spawn();
			}
			progressBar.EffectTick(new TargetInfo(Position + IntVec3.North.RotatedBy(Rotation), Map), TargetInfo.Invalid);
			MoteProgressBar mote = ((SubEffecter_ProgressBar)progressBar.children[0]).mote;
			if (mote != null)
			{
				mote.progress = 1f - Mathf.Clamp01((float)ticksRemaining / TicksToExtract);
				mote.offsetZ = ((Rotation == Rot4.North) ? 0.5f : (-0.5f));
			}
		}

		public override AcceptanceReport CanAcceptPawn(Pawn pawn)
		{
            if (!pawn.IsColonist && !pawn.IsSlaveOfColony && !pawn.IsPrisonerOfColony && (!pawn.IsColonySubhuman || !pawn.IsGhoul))
            {
                return false;
			}
			if (selectedPawn != null && selectedPawn != pawn)
			{
                return false;
			}
			if (!pawn.RaceProps.Humanlike || pawn.IsQuestLodger())
			{
                return false;
			}
			if (!PowerOn)
			{
				return "NoPower".Translate().CapitalizeFirst();
			}
			if (innerContainer.Count > 0)
			{
				return "Occupied".Translate();
			}
			if (pawn.genes == null || !pawn.genes.GenesListForReading.Any((Gene x) => x.def.passOnDirectly))
			{
                return "PawnHasNoGenes".Translate(pawn.Named("PAWN"));
			}
			if (!pawn.genes.GenesListForReading.Any((Gene x) => x.def.biostatArc >= 1))
			{
                return "PawnHasNoNonArchiteGenes".Translate(pawn.Named("PAWN"));
			}
			return true;
		}

		private void Cancel()
		{
			startTick = -1;
			selectedPawn = null;
			sustainerWorking = null;
			powerCutTicks = 0;
			innerContainer.TryDropAll(def.hasInteractionCell ? InteractionCell : Position, Map, ThingPlaceMode.Near);
		}

		private static int countMaxCapsules(Pawn pawn)
		{
			int r = 0;
			foreach (Gene gene in pawn.genes.GenesListForReading)
			{
				r += gene.def.biostatArc;
			}
			return r;
		}

		private void Finish()
		{
			startTick = -1;
			selectedPawn = null;
			sustainerWorking = null;
			powerCutTicks = 0;

			Pawn pawn = ContainedPawn;
			if (pawn == null)
			{
				return;
			}
			// Count extracted capsules
			int nCapsules = 1; // Extract Min 1
			int maxCapsules = countMaxCapsules(pawn);
			for (int i = 1; i < maxCapsules; i++)
			{
				if (Rand.Chance(0.75f))
					break;
				nCapsules += 1;
			}
			// Remove genes
			List<Gene> genesToRemove = new List<Gene>();
			foreach (Gene gene in pawn.genes.GenesListForReading)
			{
				if (gene.def.biostatArc != 0)
					genesToRemove.Add(gene);
			}
			foreach (Gene gene in genesToRemove)
			{
				pawn.genes.RemoveGene(gene);
			}

			// Kill pawn
			GeneUtility.ExtractXenogerm(pawn, Mathf.RoundToInt(NoPowerEjectCumulativeTicks * GeneTuning.GeneExtractorRegrowingDurationDaysRange.RandomInRange));
			if (!pawn.Dead)
			{
				GeneUtility.ExtractXenogerm(pawn, Mathf.RoundToInt(NoPowerEjectCumulativeTicks * GeneTuning.GeneExtractorRegrowingDurationDaysRange.RandomInRange));
			}
			if (!pawn.Dead)
			{
				pawn.Kill(null, null);
			}

			// Eject corpse
			innerContainer.TryDropAll(def.hasInteractionCell ? InteractionCell : Position, Map, ThingPlaceMode.Near);

			// Drop archite capsules
			if (nCapsules > 0)
			{
				Thing capsules = ThingMaker.MakeThing(ThingDefOf.ArchiteCapsule);
				capsules.stackCount = nCapsules;
				GenPlace.TryPlaceThing(capsules, def.hasInteractionCell ? InteractionCell : Position, Map, ThingPlaceMode.Near);
			}

			Messages.Message("ArchiteCapsuleExtractionComplete".Translate(pawn.Named("PAWN"), nCapsules), MessageTypeDefOf.PositiveEvent);
		}

		public override void TryAcceptPawn(Pawn pawn)
		{
			if (CanAcceptPawn(pawn))
			{
				selectedPawn = pawn;
				bool num = pawn.DeSpawnOrDeselect();
				if (innerContainer.TryAddOrTransfer(pawn))
				{
					startTick = Find.TickManager.TicksGame;
					ticksRemaining = TicksToExtract;
				}
				if (num)
				{
					Find.Selector.Select(pawn, playSound: false, forceDesignatorDeselect: false);
				}
			}
		}

		protected override void SelectPawn(Pawn pawn)
		{
			Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("ConfirmExtractArchiteCapsulesWillKill".Translate(pawn.Named("PAWN")), delegate
			{
				base.SelectPawn(pawn);
			}));
		}
		public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
		{
			foreach (FloatMenuOption floatMenuOption in base.GetFloatMenuOptions(selPawn))
			{
				yield return floatMenuOption;
			}
			if (!selPawn.CanReach(this, PathEndMode.InteractionCell, Danger.Deadly))
			{
				yield return new FloatMenuOption("CannotEnterBuilding".Translate(this) + ": " + "NoPath".Translate().CapitalizeFirst(), null);
			}
			else
			{
				AcceptanceReport acceptanceReport = this.CanAcceptPawn(selPawn);
				if (acceptanceReport.Accepted)
				{
					yield return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption("EnterBuilding".Translate(this), delegate ()
					{
						this.SelectPawn(selPawn);
					}, MenuOptionPriority.Default, null, null, 0f, null, null, true, 0), selPawn, this, "ReservedBy", null);
				}
				else if (base.SelectedPawn == selPawn && !selPawn.IsPrisonerOfColony)
				{
					yield return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption("EnterBuilding".Translate(this), delegate ()
					{
						selPawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.EnterBuilding, this), new JobTag?(JobTag.Misc), false);
					}, MenuOptionPriority.Default, null, null, 0f, null, null, true, 0), selPawn, this, "ReservedBy", null);
				}
				else if (!acceptanceReport.Reason.NullOrEmpty())
				{
					yield return new FloatMenuOption("CannotEnterBuilding".Translate(this) + ": " + acceptanceReport.Reason.CapitalizeFirst(), null, MenuOptionPriority.Default, null, null, 0f, null, null, true, 0);
				}
			}
		}

		public override IEnumerable<Gizmo> GetGizmos()
		{
			foreach (Gizmo gizmo in base.GetGizmos())
			{
				yield return gizmo;
			}
			if (Working)
			{
				Command_Action command_Action = new Command_Action
				{
					defaultLabel = "CommandCancelExtraction".Translate(),
					defaultDesc = "CommandCancelExtractionDesc".Translate(),
					icon = CancelIcon,
					action = Cancel,
					activateSound = SoundDefOf.Designate_Cancel
				};
				yield return command_Action;
				if (!DebugSettings.ShowDevGizmos) yield break;
				Command_Action command_Action2 = new Command_Action
				{
					defaultLabel = "DEV: Finish extraction",
					action = Finish
				};
				yield return command_Action2;
				yield break;
			}
			if (selectedPawn != null)
			{
				Command_Action command_Action3 = new Command_Action
				{
					defaultLabel = "CommandCancelLoad".Translate(),
					defaultDesc = "CommandCancelLoadDesc".Translate(),
					icon = CancelIcon,
					activateSound = SoundDefOf.Designate_Cancel,
					action = delegate
					{
						innerContainer.TryDropAll(Position, Map, ThingPlaceMode.Near);
						if (selectedPawn.CurJobDef == JobDefOf.EnterBuilding)
						{
							selectedPawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
						}
						selectedPawn = null;
						startTick = -1;
						sustainerWorking = null;
					}
				};
				yield return command_Action3;
				yield break;
			}
			Command_Action command_Action4 = new Command_Action
			{
				defaultLabel = "InsertPerson".Translate() + "...",
				defaultDesc = "InsertPersonGeneExtractorDesc".Translate(),
				icon = InsertPawnTex,
				action = delegate
				{
					List<Pawn> listPawns = new List<Pawn>();
					foreach (Pawn item in Map.mapPawns.AllPawnsSpawned)
					{
						if (item.genes != null && CanAcceptPawn(item).Accepted)
						{
							listPawns.Add(item);
						}
					}
					listPawns.SortBy(countMaxCapsules);

					List<FloatMenuOption> list = new List<FloatMenuOption>();
					foreach (Pawn pawn in listPawns)
					{
						string text = pawn.LabelShortCap + ", " + pawn.genes.XenotypeLabelCap;
						int c = countMaxCapsules(pawn);
						if (c == 1)
						{
							text = text + " (1 capsule)";
						}
						else
						{
							if (c > 10)
							{
								// actually we might extract more than 10 capsules, but probability is less than 1%,
								// so don't giving false hopes here
								c = 10;
							}
							text = text + " (1-" + c + " capsules)";
						}
						list.Add(new FloatMenuOption(text, delegate
						{
							SelectPawn(pawn);
						}, pawn, Color.white));
					}
					if (!list.Any())
					{
						list.Add(new FloatMenuOption("NoExtractablePawns".Translate(), null));
					}
					Find.WindowStack.Add(new FloatMenu(list));
				}
			};
			if (!PowerOn)
			{
				command_Action4.Disable("NoPower".Translate().CapitalizeFirst());
			}
			yield return command_Action4;
		}

		public override void DynamicDrawPhaseAt(DrawPhase phase, Vector3 drawLoc, bool flip = false)
		{
			base.DynamicDrawPhaseAt(phase, drawLoc, flip);
			if (Working && ContainedPawn != null)
			{
				drawLoc.x += BuildingRotationOffsetX(Rotation);
				drawLoc.z += BuildingRotationOffsetY(Rotation);
				ContainedPawn.Drawer.renderer.DynamicDrawPhaseAt(phase, drawLoc + PawnDrawOffset, null, true);
			}
		}

		private float BuildingRotationOffsetX (Rot4 rot4)
		{
			float offset = 0f;
			if (rot4 == Rot4.North)
			{
				offset = 1f;
			}
			if (rot4 == Rot4.South)
			{
				offset = -1f;
			}
			return offset;
		}

		private float BuildingRotationOffsetY(Rot4 rot4)
		{
			float offset = 0;
			if (rot4 == Rot4.East)
			{
				offset = -1f;
			}
			if (rot4 == Rot4.West)
			{
				offset = 1f;
			}
			return offset;
		}

		public override string GetInspectString()
		{
			string text = base.GetInspectString();
			if (selectedPawn != null && innerContainer.Count == 0)
			{
				if (!text.NullOrEmpty())
				{
					text += "\n";
				}
				text += "WaitingForPawn".Translate(selectedPawn.Named("PAWN")).Resolve();
			}
			else if (Working && ContainedPawn != null)
			{
				if (!text.NullOrEmpty())
				{
					text += "\n";
				}
				text = text + "ExtractingXenogermFrom".Translate(ContainedPawn.Named("PAWN")).Resolve() + "\n";
				text = ((!PowerOn) ? (text + "ExtractionPausedNoPower".Translate((NoPowerEjectCumulativeTicks - powerCutTicks).ToStringTicksToPeriod().Named("TIME")).Colorize(ColorLibrary.RedReadable)) : (text + "DurationLeft".Translate(ticksRemaining.ToStringTicksToPeriod()).Resolve()));
			}
			return text;
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref ticksRemaining, "ticksRemaining", 0);
			Scribe_Values.Look(ref powerCutTicks, "powerCutTicks", 0);
		}
	}
}
