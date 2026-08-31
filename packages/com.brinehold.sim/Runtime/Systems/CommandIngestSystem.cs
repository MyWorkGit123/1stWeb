using System.Collections.Generic;
using Brinehold.Core.Collections;
using Brinehold.Core.Math;
using Brinehold.Sim.Commands;
using Brinehold.Sim.Content;
using Brinehold.Sim.World;

namespace Brinehold.Sim.Systems
{
    /// <summary>
    /// Validates and applies queued player commands.
    ///
    /// This is the only door into the simulation from outside, and therefore the whole of the
    /// command-validation half of the anti-cheat design (MULTIPLAYER_ARCHITECTURE.md section 7.2).
    /// Every command is checked for ownership, entity liveness, affordability at the execution
    /// tick, population room, placement legality and target validity. An invalid command is
    /// dropped and reported, never clamped into a legal one: a clamped command is indistinguishable
    /// from a successful cheat.
    /// </summary>
    public sealed class CommandIngestSystem : ISimSystem
    {
        public void Execute(SimWorld world)
        {
            List<Command> pending = world.PendingCommands;
            if (pending.Count == 0) return;

            // Deterministic execution order: by player, then by the player's own sequence number.
            pending.Sort(CompareCommands);

            for (int c = 0; c < pending.Count; c++)
            {
                Command command = pending[c];
                RejectReason reason = Apply(world, command);
                if (reason != RejectReason.None)
                    world.Events.Add(SimEvent.Rejected(command.PlayerId, command.Sequence, reason));
            }

            pending.Clear();
        }

        private static int CompareCommands(Command a, Command b)
        {
            if (a.PlayerId != b.PlayerId) return a.PlayerId < b.PlayerId ? -1 : 1;
            if (a.Sequence != b.Sequence) return a.Sequence < b.Sequence ? -1 : 1;
            return 0;
        }

        private static RejectReason Apply(SimWorld world, Command command)
        {
            if (world.MatchOver) return RejectReason.MatchOver;
            if (command.PlayerId >= world.Players.Length) return RejectReason.UnknownPlayer;
            if (world.Players[command.PlayerId].Defeated) return RejectReason.PlayerDefeated;
            if (command.EntityCount > Command.MaxEntities) return RejectReason.TooManyEntities;

            switch (command.Type)
            {
                case CommandType.Move: return ApplyMove(world, command);
                case CommandType.Harvest: return ApplyHarvest(world, command);
                case CommandType.Build: return ApplyBuild(world, command);
                case CommandType.Train: return ApplyTrain(world, command);
                case CommandType.Attack: return ApplyAttack(world, command);
                case CommandType.Stop: return ApplyStop(world, command);
                case CommandType.CancelTraining: return ApplyCancelTraining(world, command);
                default: return RejectReason.InvalidTarget;
            }
        }

        /// <summary>
        /// Ownership and liveness check applied to every entity a command names. This is the check
        /// that makes "move the enemy's units" impossible rather than merely unusual.
        /// </summary>
        private static bool OwnsLiveEntity(SimWorld world, Command command, int slot, out int index)
        {
            index = -1;
            EntityId id = command.Entities[slot];
            if (!world.Entities.IsAlive(id)) return false;
            if (world.Entities.Owner[id.Index] != command.PlayerId) return false;
            index = id.Index;
            return true;
        }

        private static RejectReason ApplyMove(SimWorld world, Command command)
        {
            if (command.EntityCount <= 0) return RejectReason.NoEntities;
            if (!world.Nav.InBounds(command.TargetCellX, command.TargetCellY)) return RejectReason.OutOfBounds;

            Fix2 destination = world.Nav.CellCentre(world.Nav.Index(command.TargetCellX, command.TargetCellY));

            bool any = false;
            bool sawForeign = false;
            for (int s = 0; s < command.EntityCount; s++)
            {
                if (!OwnsLiveEntity(world, command, s, out int i)) { sawForeign = true; continue; }
                if (world.Entities.Domain[i] == MovementDomain.Static) continue;

                world.SetJob(i, JobType.MoveTo, destination, EntityId.None);
                if (!world.RequestPath(i, destination))
                    world.Entities.Job[i] = JobType.Idle;
                any = true;
            }

            if (!any) return sawForeign ? RejectReason.NotOwner : RejectReason.NoEntities;
            return RejectReason.None;
        }

        private static RejectReason ApplyHarvest(SimWorld world, Command command)
        {
            if (command.EntityCount <= 0) return RejectReason.NoEntities;

            EntityId node = command.TargetEntity;
            if (!world.Entities.IsAlive(node)) return RejectReason.InvalidTarget;
            if (world.Entities.Kind[node.Index] != EntityKind.ResourceNode) return RejectReason.InvalidTarget;
            if (world.Entities.NodeRemaining[node.Index] <= 0) return RejectReason.InvalidTarget;

            Fix2 nodePosition = world.Entities.Position[node.Index];
            bool any = false;
            bool sawForeign = false;

            for (int s = 0; s < command.EntityCount; s++)
            {
                if (!OwnsLiveEntity(world, command, s, out int i)) { sawForeign = true; continue; }
                if (world.Entities.Kind[i] != EntityKind.Worker) continue;

                world.Entities.HomeNode[i] = node;
                world.SetJob(i, JobType.MoveToHarvest, nodePosition, node);
                if (!world.RequestPath(i, nodePosition))
                {
                    // Already standing next to it is a legitimate no-path case.
                    if (!SimRange.InReach(world.Entities, i, node.Index, SimRange.InteractionReach))
                        world.Entities.Job[i] = JobType.Idle;
                }
                any = true;
            }

            if (!any) return sawForeign ? RejectReason.NotOwner : RejectReason.WrongEntityKind;
            return RejectReason.None;
        }

        private static RejectReason ApplyBuild(SimWorld world, Command command)
        {
            if (command.EntityCount <= 0) return RejectReason.NoEntities;
            if (command.Building == BuildingType.None) return RejectReason.InvalidTarget;

            // At least one live, owned worker must be available to build it.
            bool hasBuilder = false;
            for (int s = 0; s < command.EntityCount; s++)
            {
                if (!OwnsLiveEntity(world, command, s, out int i)) continue;
                if (world.Entities.Kind[i] == EntityKind.Worker) { hasBuilder = true; break; }
            }
            if (!hasBuilder) return RejectReason.WrongEntityKind;

            if (!BuildPlacement.IsLegal(world, command.Building, command.TargetCellX, command.TargetCellY, out RejectReason placement))
                return placement;

            PrototypeContent.BuildingStats stats = PrototypeContent.ForBuilding(command.Building);
            PlayerState player = world.Players[command.PlayerId];
            if (!player.CanAfford(stats.CostWood, stats.CostFood, stats.CostStone, stats.CostCoin))
                return RejectReason.NotEnoughResources;

            player.Spend(stats.CostWood, stats.CostFood, stats.CostStone, stats.CostCoin);

            EntityId site = world.SpawnBuilding(command.Building, command.PlayerId,
                command.TargetCellX, command.TargetCellY, completed: false);

            Fix2 sitePosition = world.Entities.Position[site.Index];
            for (int s = 0; s < command.EntityCount; s++)
            {
                if (!OwnsLiveEntity(world, command, s, out int i)) continue;
                if (world.Entities.Kind[i] != EntityKind.Worker) continue;

                world.SetJob(i, JobType.MoveToBuild, sitePosition, site);
                if (!world.RequestPath(i, sitePosition))
                {
                    if (!SimRange.InReach(world.Entities, i, site.Index, SimRange.InteractionReach))
                        world.Entities.Job[i] = JobType.Idle;
                }
            }

            return RejectReason.None;
        }

        private static RejectReason ApplyTrain(SimWorld world, Command command)
        {
            if (command.EntityCount <= 0) return RejectReason.NoEntities;
            if (!OwnsLiveEntity(world, command, 0, out int b)) return RejectReason.NotOwner;

            EntityStore store = world.Entities;
            if (store.Kind[b] != EntityKind.Building) return RejectReason.WrongEntityKind;
            if (store.UnderConstruction[b]) return RejectReason.BuildingUnderConstruction;
            if (!PrototypeContent.CanTrain(store.Building[b], command.TrainKind)) return RejectReason.CannotTrainHere;

            // One queue per building in the prototype: a second kind cannot be interleaved.
            if (store.TrainingQueued[b] > 0 && store.TrainingKind[b] != command.TrainKind)
                return RejectReason.CannotTrainHere;

            PrototypeContent.UnitStats stats = PrototypeContent.ForKind(command.TrainKind);
            PlayerState player = world.Players[command.PlayerId];

            if (!player.CanAfford(stats.CostWood, stats.CostFood, stats.CostStone, stats.CostCoin))
                return RejectReason.NotEnoughResources;

            int queuedPopulation = QueuedPopulation(world, command.PlayerId);
            if (player.PopulationUsed + queuedPopulation + stats.PopulationCost > player.PopulationCap)
                return RejectReason.PopulationCapReached;

            player.Spend(stats.CostWood, stats.CostFood, stats.CostStone, stats.CostCoin);

            store.TrainingKind[b] = command.TrainKind;
            store.TrainingQueued[b]++;
            if (store.TrainingQueued[b] == 1) store.TrainingTimer[b] = stats.TrainTicks;

            return RejectReason.None;
        }

        private static RejectReason ApplyCancelTraining(SimWorld world, Command command)
        {
            if (command.EntityCount <= 0) return RejectReason.NoEntities;
            if (!OwnsLiveEntity(world, command, 0, out int b)) return RejectReason.NotOwner;

            EntityStore store = world.Entities;
            if (store.Kind[b] != EntityKind.Building) return RejectReason.WrongEntityKind;
            if (store.TrainingQueued[b] <= 0) return RejectReason.NothingQueued;

            PrototypeContent.UnitStats stats = PrototypeContent.ForKind(store.TrainingKind[b]);
            world.Players[command.PlayerId].Refund(stats.CostWood, stats.CostFood, stats.CostStone, stats.CostCoin);

            store.TrainingQueued[b]--;
            store.TrainingTimer[b] = store.TrainingQueued[b] > 0 ? stats.TrainTicks : 0;
            return RejectReason.None;
        }

        private static RejectReason ApplyAttack(SimWorld world, Command command)
        {
            if (command.EntityCount <= 0) return RejectReason.NoEntities;

            EntityId target = command.TargetEntity;
            if (!world.Entities.IsAlive(target)) return RejectReason.InvalidTarget;
            if (world.Entities.Kind[target.Index] == EntityKind.ResourceNode) return RejectReason.InvalidTarget;
            if (!world.AreHostile(command.PlayerId, world.Entities.Owner[target.Index])) return RejectReason.InvalidTarget;

            Fix2 targetPosition = world.Entities.Position[target.Index];
            bool any = false;
            bool sawForeign = false;

            for (int s = 0; s < command.EntityCount; s++)
            {
                if (!OwnsLiveEntity(world, command, s, out int i)) { sawForeign = true; continue; }
                if (world.Entities.AttackDamage[i] <= Fix64.Zero) continue;
                if (world.Entities.Domain[i] == MovementDomain.Static) continue;

                world.SetJob(i, JobType.MoveToAttack, targetPosition, target);
                world.RequestPath(i, targetPosition);
                any = true;
            }

            if (!any) return sawForeign ? RejectReason.NotOwner : RejectReason.WrongEntityKind;
            return RejectReason.None;
        }

        private static RejectReason ApplyStop(SimWorld world, Command command)
        {
            if (command.EntityCount <= 0) return RejectReason.NoEntities;

            bool any = false;
            for (int s = 0; s < command.EntityCount; s++)
            {
                if (!OwnsLiveEntity(world, command, s, out int i)) continue;
                world.Entities.ClearPath(i);
                world.SetJob(i, JobType.Idle, world.Entities.Position[i], EntityId.None);
                any = true;
            }

            return any ? RejectReason.None : RejectReason.NotOwner;
        }

        /// <summary>Population already committed to unfinished training orders.</summary>
        private static int QueuedPopulation(SimWorld world, byte player)
        {
            EntityStore store = world.Entities;
            int total = 0;
            int count = store.Count;
            for (int b = 1; b < count; b++)
            {
                if (!store.Alive[b]) continue;
                if (store.Kind[b] != EntityKind.Building) continue;
                if (store.Owner[b] != player) continue;
                if (store.TrainingQueued[b] <= 0) continue;
                total += store.TrainingQueued[b] * PrototypeContent.ForKind(store.TrainingKind[b]).PopulationCost;
            }
            return total;
        }
    }
}
