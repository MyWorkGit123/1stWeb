using System.Collections.Generic;
using Brinehold.Core.Collections;
using Brinehold.Net.Client;
using Brinehold.Sim.Content;
using Brinehold.Sim.World;

namespace Brinehold.Client.Hud
{
    /// <summary>
    /// Everything the HUD displays, derived from the replica.
    ///
    /// Every number here came from the server. The client computes nothing about resources,
    /// population or health — it formats what it was told. That is the point: a player who edits
    /// this in memory changes their own display and the next authoritative delta puts it back.
    /// </summary>
    public sealed class HudModel
    {
        private readonly ReplicaWorld _replica;

        public HudModel(ReplicaWorld replica) => _replica = replica;

        public int Wood => _replica.Wood;
        public int Food => _replica.Food;
        public int Stone => _replica.Stone;
        public int Coin => _replica.Coin;
        public int PopulationUsed => _replica.PopulationUsed;
        public int PopulationCap => _replica.PopulationCap;
        public bool PopulationBlocked => _replica.PopulationUsed >= _replica.PopulationCap;

        public bool MatchOver => _replica.MatchOver;
        public bool Won => _replica.LocalPlayerWon;

        /// <summary>Match time as mm:ss, from the authoritative tick rather than a local clock.</summary>
        public string MatchClock()
        {
            uint seconds = _replica.Tick / SimConstants.TicksPerSecond;
            return $"{seconds / 60:00}:{seconds % 60:00}";
        }

        public bool CanAfford(BuildingType type)
        {
            PrototypeContent.BuildingStats stats = PrototypeContent.ForBuilding(type);
            return Wood >= stats.CostWood && Food >= stats.CostFood
                && Stone >= stats.CostStone && Coin >= stats.CostCoin;
        }

        public bool CanAfford(EntityKind kind)
        {
            PrototypeContent.UnitStats stats = PrototypeContent.ForKind(kind);
            return Wood >= stats.CostWood && Food >= stats.CostFood
                && Stone >= stats.CostStone && Coin >= stats.CostCoin;
        }

        /// <summary>
        /// Why a build button is greyed out. Returning a reason rather than a bool is the difference
        /// between a UI that teaches the game and one that just refuses.
        /// </summary>
        public string? BuildBlockedReason(BuildingType type)
        {
            PrototypeContent.BuildingStats stats = PrototypeContent.ForBuilding(type);
            if (Wood < stats.CostWood) return $"Need {stats.CostWood - Wood} more wood";
            if (Stone < stats.CostStone) return $"Need {stats.CostStone - Stone} more stone";
            if (Food < stats.CostFood) return $"Need {stats.CostFood - Food} more food";
            if (Coin < stats.CostCoin) return $"Need {stats.CostCoin - Coin} more coin";
            return null;
        }

        public string? TrainBlockedReason(EntityKind kind)
        {
            PrototypeContent.UnitStats stats = PrototypeContent.ForKind(kind);
            if (PopulationUsed + stats.PopulationCost > PopulationCap) return "Build more housing first";
            if (Food < stats.CostFood) return $"Need {stats.CostFood - Food} more food";
            if (Wood < stats.CostWood) return $"Need {stats.CostWood - Wood} more wood";
            if (Coin < stats.CostCoin) return $"Need {stats.CostCoin - Coin} more coin";
            return null;
        }

        /// <summary>Counts of the local player's units, for the army and worker readouts.</summary>
        public void CountOwnUnits(out int workers, out int soldiers, out int ships, out int idleWorkers)
        {
            workers = soldiers = ships = idleWorkers = 0;
            foreach (ReplicaWorld.Entity entity in _replica.Entities)
            {
                if (entity.Owner != _replica.LocalPlayer) continue;
                switch (entity.Kind)
                {
                    case EntityKind.Worker:
                        workers++;
                        if (entity.State.Value.Job == JobType.Idle) idleWorkers++;
                        break;
                    case EntityKind.Soldier: soldiers++; break;
                    case EntityKind.Ship: ships++; break;
                }
            }
        }

        /// <summary>A one-line description of a selected entity, for the selection panel.</summary>
        public string Describe(EntityId id)
        {
            if (!_replica.TryGet(id, out ReplicaWorld.Entity entity)) return "Unknown";

            string owner = entity.Owner == _replica.LocalPlayer ? "Yours" : $"Player {entity.Owner + 1}";
            switch (entity.Kind)
            {
                case EntityKind.Worker: return $"Deckhand ({owner}) — {DescribeJob(entity.State.Value.Job)}";
                case EntityKind.Soldier: return $"Cutthroat ({owner}) — {DescribeJob(entity.State.Value.Job)}";
                case EntityKind.Ship: return $"Cutter ({owner})";
                case EntityKind.Building:
                    string state = entity.UnderConstruction ? " — under construction" : string.Empty;
                    return $"{DescribeBuilding(entity.Building)} ({owner}){state}";
                case EntityKind.ResourceNode: return DescribeNode(entity.Node);
                default: return "Unknown";
            }
        }

        private static string DescribeJob(JobType job)
        {
            switch (job)
            {
                case JobType.Idle: return "idle";
                case JobType.MoveTo: return "moving";
                case JobType.MoveToHarvest: return "walking to work";
                case JobType.Harvesting: return "harvesting";
                case JobType.Delivering: return "carrying a load";
                case JobType.MoveToBuild: return "walking to a site";
                case JobType.Building: return "building";
                case JobType.MoveToAttack: return "advancing";
                case JobType.Attacking: return "fighting";
                default: return "idle";
            }
        }

        private static string DescribeBuilding(BuildingType type)
        {
            switch (type)
            {
                case BuildingType.Warehouse: return "Warehouse";
                case BuildingType.House: return "Longhouse";
                case BuildingType.LumberCamp: return "Lumber Camp";
                case BuildingType.FishingWharf: return "Fishing Wharf";
                case BuildingType.Dock: return "Dock";
                default: return "Building";
            }
        }

        private static string DescribeNode(ResourceNodeType type)
        {
            switch (type)
            {
                case ResourceNodeType.Forest: return "Forest";
                case ResourceNodeType.FishShoal: return "Fish shoal";
                case ResourceNodeType.StoneOutcrop: return "Stone outcrop";
                default: return "Resource";
            }
        }

        /// <summary>Human-readable text for a server rejection, so the UI can say what went wrong.</summary>
        public static string Explain(Brinehold.Sim.Commands.RejectReason reason)
        {
            switch (reason)
            {
                case Brinehold.Sim.Commands.RejectReason.NotEnoughResources: return "Not enough resources";
                case Brinehold.Sim.Commands.RejectReason.PopulationCapReached: return "Build more housing first";
                case Brinehold.Sim.Commands.RejectReason.IllegalPlacement: return "Cannot build there";
                case Brinehold.Sim.Commands.RejectReason.OutOfBounds: return "Outside the map";
                case Brinehold.Sim.Commands.RejectReason.NotOwner: return "That is not yours";
                case Brinehold.Sim.Commands.RejectReason.EntityDead: return "That no longer exists";
                case Brinehold.Sim.Commands.RejectReason.InvalidTarget: return "Invalid target";
                case Brinehold.Sim.Commands.RejectReason.CannotTrainHere: return "That building cannot train this";
                case Brinehold.Sim.Commands.RejectReason.BuildingUnderConstruction: return "Still under construction";
                case Brinehold.Sim.Commands.RejectReason.RateLimited: return "Too many orders at once";
                case Brinehold.Sim.Commands.RejectReason.MatchOver: return "The match is over";
                default: return "Order refused";
            }
        }
    }
}
