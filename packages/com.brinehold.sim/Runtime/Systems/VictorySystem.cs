using Brinehold.Sim.World;

namespace Brinehold.Sim.Systems
{
    /// <summary>
    /// Strike the Colours: a player is defeated when they hold no settlement core, and the match
    /// ends when only one team still stands.
    ///
    /// Writes: PlayerState.Defeated, PlayerState.Victorious, match result.
    ///
    /// The prototype implements this one condition. The other five in GAME_DESIGN.md section 18
    /// arrive in M12, and each will be a separate class behind the same interface so that a lobby
    /// can enable any combination.
    /// </summary>
    public sealed class VictorySystem : ISimSystem
    {
        public void Execute(SimWorld world)
        {
            if (world.MatchOver) return;

            EntityStore store = world.Entities;
            int playerCount = world.Players.Length;

            System.Span<bool> hasCore = stackalloc bool[playerCount];

            int count = store.Count;
            for (int i = 1; i < count; i++)
            {
                if (!store.Alive[i]) continue;
                if (store.Kind[i] != EntityKind.Building) continue;
                if (store.Building[i] != BuildingType.Warehouse) continue;
                byte owner = store.Owner[i];
                if (owner < playerCount) hasCore[owner] = true;
            }

            for (int p = 0; p < playerCount; p++)
            {
                PlayerState player = world.Players[p];
                if (player.Defeated || hasCore[p]) continue;

                player.Defeated = true;
                world.Events.Add(new SimEvent
                {
                    Type = SimEventType.PlayerDefeated,
                    Player = (byte)p
                });
            }

            // Count the teams that still have an undefeated player.
            int survivingTeam = -1;
            int survivingTeamCount = 0;
            for (int p = 0; p < playerCount; p++)
            {
                if (world.Players[p].Defeated) continue;
                int team = world.Players[p].Team;
                if (team == survivingTeam) continue;

                bool alreadyCounted = false;
                for (int q = 0; q < p; q++)
                    if (!world.Players[q].Defeated && world.Players[q].Team == team) { alreadyCounted = true; break; }
                if (alreadyCounted) continue;

                survivingTeam = team;
                survivingTeamCount++;
            }

            if (survivingTeamCount <= 1)
            {
                for (int p = 0; p < playerCount; p++)
                    if (!world.Players[p].Defeated) world.Players[p].Victorious = true;
                world.SetMatchOver(survivingTeamCount == 1 ? survivingTeam : -1);
            }
        }
    }
}
