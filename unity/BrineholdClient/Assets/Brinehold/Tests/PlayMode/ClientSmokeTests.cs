using System.Collections;
using Brinehold.Unity.Boot;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Brinehold.Unity.Tests
{
    /// <summary>
    /// Play-mode tests for the things only the real Unity runtime can answer.
    ///
    /// Everything about the simulation, the networking and the client's logic is already covered by
    /// 231 headless tests that need no editor. These cover the remaining surface: does the scene
    /// actually construct, do views appear and move, is there geometry, does the fog texture exist.
    ///
    /// They are written to run in batch mode, so verifying a change is one command and produces a
    /// pass/fail rather than a description of what somebody thought they saw on screen:
    ///
    ///   Unity -batchmode -projectPath unity/BrineholdClient -runTests \
    ///         -testPlatform PlayMode -testResults results.xml -logFile unity.log
    /// </summary>
    public class ClientSmokeTests
    {
        private GameObject _harness;

        [TearDown]
        public void TearDown()
        {
            if (_harness != null) Object.Destroy(_harness);
        }

        /// <summary>Builds the prototype scene and lets it run for a number of frames.</summary>
        private IEnumerator StartMatch(int frames = 10)
        {
            _harness = new GameObject("BrineholdTestHarness");
            _harness.AddComponent<PrototypeSceneSetup>();

            // Awake wires the scene; Start builds the match on the following frame.
            for (int i = 0; i < frames; i++) yield return null;
        }

        private GameBootstrap Game => _harness.GetComponent<GameBootstrap>();

        [UnityTest]
        public IEnumerator TheSceneBuildsAndTheMatchStarts()
        {
            yield return StartMatch();

            Assert.IsNotNull(Game, "PrototypeSceneSetup did not add a GameBootstrap");
            Assert.IsNotNull(Game.Host, "the match host was never created");
            Assert.IsNotNull(Game.Replica, "the client replica was never created");
            Assert.IsNotNull(Game.SceneCamera, "no camera was built");
        }

        /// <summary>
        /// Regression guard for a real bug: GameBootstrap used to initialise in Awake, which runs
        /// synchronously inside AddComponent — before PrototypeSceneSetup had assigned Terrain,
        /// Views and Fog. The guards then skipped and the match ran with no visible world at all.
        /// </summary>
        [UnityTest]
        public IEnumerator TheSceneReferencesAreWiredBeforeTheMatchIsBuilt()
        {
            yield return StartMatch();

            Assert.IsNotNull(Game.Terrain, "the terrain builder was not wired");
            Assert.IsNotNull(Game.Views, "the view pool was not wired");
            Assert.IsNotNull(Game.Fog, "the fog renderer was not wired");
        }

        [UnityTest]
        public IEnumerator TheTerrainHasActualGeometry()
        {
            yield return StartMatch();

            var filters = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None);
            Assert.Greater(filters.Length, 0, "no terrain meshes were built");

            int triangles = 0;
            foreach (MeshFilter filter in filters)
                if (filter.sharedMesh != null) triangles += filter.sharedMesh.triangles.Length / 3;

            Assert.Greater(triangles, 100, $"the terrain has only {triangles} triangles");
        }

        /// <summary>
        /// Regression guard for a real bug: views are cloned from inactive prefab templates, so a
        /// clone is inactive too. Without an explicit activation every unit existed, moved and
        /// fought while being completely invisible.
        /// </summary>
        [UnityTest]
        public IEnumerator EveryVisibleEntityHasAnActiveViewInTheScene()
        {
            yield return StartMatch(30);

            int known = Game.Replica.EntityCount;
            Assert.Greater(known, 10, $"the client only knows about {known} entities");

            int activeViews = 0;
            foreach (var entity in Game.Replica.Entities)
            {
                if (!Game.Views.TryGetView(entity.Id.Raw, out EntityView view)) continue;
                Assert.IsTrue(view.gameObject.activeSelf,
                    $"the view for {entity.Kind} {entity.Id} exists but is inactive — it would be invisible");
                activeViews++;
            }

            Assert.Greater(activeViews, 10, $"only {activeViews} of {known} known entities have a view");
        }

        [UnityTest]
        public IEnumerator TheFogTextureIsCreated()
        {
            yield return StartMatch();

            Assert.IsNotNull(Game.Fog, "no fog renderer");
            Assert.IsNotNull(Game.Fog.FogQuad, "the fog quad was never built");
        }

        [UnityTest]
        public IEnumerator TheSimulationAdvancesInRealTime()
        {
            yield return StartMatch();

            uint before = Game.Host.World.Tick;
            yield return new WaitForSeconds(1.0f);
            uint after = Game.Host.World.Tick;

            // Twenty ticks a second, with generous slack for a loaded batch-mode machine.
            Assert.Greater(after, before, "the simulation never advanced");
            Assert.Greater(after - before, 5u, $"only {after - before} ticks in a second");
        }

        [UnityTest]
        public IEnumerator OrderedWorkersActuallyMoveOnScreen()
        {
            yield return StartMatch(20);

            Brinehold.Core.Collections.EntityId worker = default;
            foreach (var entity in Game.Replica.Entities)
            {
                if (entity.Owner != Game.LocalPlayer) continue;
                if (entity.Kind != Brinehold.Sim.World.EntityKind.Worker) continue;
                worker = entity.Id;
                break;
            }
            Assert.IsFalse(worker.IsNone, "the client sees no workers of its own");

            Assert.IsTrue(Game.Views.TryGetView(worker.Raw, out EntityView view));
            Vector3 before = view.transform.position;

            Game.Issue(Brinehold.Sim.Commands.Command.Move(
                Game.LocalPlayer, 0, new[] { worker },
                Brinehold.Sim.Map.PrototypeMap.StartCellX[0] + 18,
                Brinehold.Sim.Map.PrototypeMap.StartCellY[0] + 18));

            yield return new WaitForSeconds(4.0f);

            float moved = Vector3.Distance(before, view.transform.position);
            Assert.Greater(moved, 2.0f, $"the worker's view only moved {moved:0.0} m");
        }

        [UnityTest]
        public IEnumerator TheHudReflectsTheServersEconomy()
        {
            yield return StartMatch(20);

            Assert.AreEqual(Game.Host.World.Players[Game.LocalPlayer].Wood, Game.Hud.Wood,
                "the HUD disagrees with the server about wood");
            Assert.AreEqual(Game.Host.World.Players[Game.LocalPlayer].PopulationCap, Game.Hud.PopulationCap,
                "the HUD disagrees with the server about the population cap");
        }

        [UnityTest]
        public IEnumerator SelectionPicksUpAWorker()
        {
            yield return StartMatch(20);

            Brinehold.Core.Math.Fix2 min = Brinehold.Core.Math.Fix2.FromInt(
                Brinehold.Sim.Map.PrototypeMap.StartCellX[0] - 20,
                Brinehold.Sim.Map.PrototypeMap.StartCellY[0] - 20);
            Brinehold.Core.Math.Fix2 max = Brinehold.Core.Math.Fix2.FromInt(
                Brinehold.Sim.Map.PrototypeMap.StartCellX[0] + 20,
                Brinehold.Sim.Map.PrototypeMap.StartCellY[0] + 20);

            var boxed = Game.Selection.BoxSelect(min, max);
            Game.Selection.SetMany(boxed);

            Assert.AreEqual(10, Game.Selection.Count, "a box over the starting area did not catch ten workers");
        }

        [UnityTest]
        public IEnumerator NothingLogsAnErrorDuringAQuietMinuteOfPlay()
        {
            yield return StartMatch();

            // LogAssert fails the test if any error or exception is logged and not expected.
            yield return new WaitForSeconds(3.0f);

            Assert.IsFalse(Game.Replica.MatchOver, "the match ended on its own within three seconds");
        }
    }
}
