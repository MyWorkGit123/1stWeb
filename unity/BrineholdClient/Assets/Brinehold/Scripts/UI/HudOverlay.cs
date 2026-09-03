using Brinehold.Net;
using Brinehold.Sim.World;
using UnityEngine;

namespace Brinehold.Unity.Boot
{
    /// <summary>
    /// The prototype HUD, drawn with IMGUI.
    ///
    /// Deliberately immediate-mode: the prototype exists to prove networking, simulation and
    /// authority, and an IMGUI overlay gets all the information on screen with no prefab wiring, no
    /// canvas setup and nothing to break. The production HUD described in GAME_DESIGN.md section 22
    /// is a UI Toolkit rebuild in M5 — it reads from exactly the same HudModel, so replacing this
    /// file changes no logic.
    ///
    /// Every number displayed came from the server. The client computes none of them.
    /// </summary>
    public sealed class HudOverlay : MonoBehaviour
    {
        public GameBootstrap Game;
        public bool ShowNetGraph = true;

        private GUIStyle _panel;
        private GUIStyle _label;
        private GUIStyle _heading;
        private string _lastRejection = string.Empty;
        private float _lastRejectionTime;

        private void Update()
        {
            if (Game == null || Game.Replica == null) return;

            if (UnityEngine.Input.GetKeyDown(KeyCode.F3)) ShowNetGraph = !ShowNetGraph;

            // Surface the most recent server rejection so the player is told why an order failed.
            if (Game.Replica.Rejections.Count > 0)
            {
                var last = Game.Replica.Rejections[Game.Replica.Rejections.Count - 1];
                _lastRejection = Brinehold.Client.Hud.HudModel.Explain(last.Reason);
                _lastRejectionTime = Time.time;
                Game.Replica.Rejections.Clear();
            }
        }

        private void OnGUI()
        {
            if (Game == null || Game.Replica == null) return;
            EnsureStyles();

            DrawResourceBar();
            DrawSelectionPanel();
            DrawPlacementPrompt();
            DrawRejection();
            if (ShowNetGraph) DrawNetGraph();
            DrawMatchResult();
        }

        private void EnsureStyles()
        {
            if (_panel != null) return;

            var background = new Texture2D(1, 1);
            background.SetPixel(0, 0, new Color(0.05f, 0.06f, 0.08f, 0.82f));
            background.Apply();

            _panel = new GUIStyle(GUI.skin.box) { normal = { background = background } };
            _label = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = Color.white } };
            _heading = new GUIStyle(_label) { fontStyle = FontStyle.Bold };
        }

        private void DrawResourceBar()
        {
            GUILayout.BeginArea(new Rect(8, 8, 620, 30), _panel);
            GUILayout.BeginHorizontal();

            GUILayout.Label($"Wood {Game.Hud.Wood}", _label, GUILayout.Width(100));
            GUILayout.Label($"Food {Game.Hud.Food}", _label, GUILayout.Width(100));
            GUILayout.Label($"Stone {Game.Hud.Stone}", _label, GUILayout.Width(100));
            GUILayout.Label($"Coin {Game.Hud.Coin}", _label, GUILayout.Width(100));

            Color previous = GUI.color;
            if (Game.Hud.PopulationBlocked) GUI.color = new Color(1f, 0.6f, 0.4f);
            GUILayout.Label($"Pop {Game.Hud.PopulationUsed}/{Game.Hud.PopulationCap}", _label, GUILayout.Width(90));
            GUI.color = previous;

            GUILayout.Label(Game.Hud.MatchClock(), _label, GUILayout.Width(60));

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawSelectionPanel()
        {
            if (Game.Selection.IsEmpty) return;

            GUILayout.BeginArea(new Rect(8, Screen.height - 130, 380, 122), _panel);
            GUILayout.Label($"Selected: {Game.Selection.Count}", _heading);

            int shown = 0;
            foreach (var id in Game.Selection.Selected)
            {
                if (shown++ >= 4) { GUILayout.Label($"…and {Game.Selection.Count - 4} more", _label); break; }
                GUILayout.Label(Game.Hud.Describe(id), _label);
            }

            GUILayout.EndArea();
        }

        private void DrawPlacementPrompt()
        {
            if (!Game.Placement.Active) return;

            GUILayout.BeginArea(new Rect(Screen.width / 2 - 150, 46, 300, 52), _panel);
            GUILayout.Label($"Placing: {Game.Placement.Type}", _heading);

            Color previous = GUI.color;
            GUI.color = Game.Placement.Legal ? new Color(0.5f, 1f, 0.6f) : new Color(1f, 0.5f, 0.45f);
            GUILayout.Label(Game.Placement.Legal
                ? $"Left click to build at {Game.Placement.CellX}, {Game.Placement.CellY}"
                : Game.Placement.Reason ?? "Cannot build here", _label);
            GUI.color = previous;

            GUILayout.EndArea();
        }

        private void DrawRejection()
        {
            if (string.IsNullOrEmpty(_lastRejection)) return;
            if (Time.time - _lastRejectionTime > 3f) return;

            Color previous = GUI.color;
            GUI.color = new Color(1f, 0.55f, 0.45f);
            GUILayout.BeginArea(new Rect(Screen.width / 2 - 120, Screen.height - 170, 240, 28), _panel);
            GUILayout.Label(_lastRejection, _label);
            GUILayout.EndArea();
            GUI.color = previous;
        }

        /// <summary>
        /// The measurement the prototype's acceptance criteria are stated in. If the correction
        /// count is climbing while units walk, intent replication has regressed.
        /// </summary>
        private void DrawNetGraph()
        {
            NetStats stats = Game.Host.Replication.Stats;
            byte player = Game.LocalPlayer;
            uint tick = Game.Host.World.Tick;

            GUILayout.BeginArea(new Rect(Screen.width - 300, 8, 292, 150), _panel);
            GUILayout.Label("Network (F3)", _heading);
            GUILayout.Label($"tick {tick}", _label);
            GUILayout.Label($"{stats.BytesPerSecond(player, tick):0.0} B/s  ({stats.TotalBytes(player)} B total)", _label);
            GUILayout.Label($"lifecycle {stats.MessageCount(player, NetStats.Category.Lifecycle)}", _label);
            GUILayout.Label($"intent    {stats.MessageCount(player, NetStats.Category.Intent)}", _label);
            GUILayout.Label($"correction {stats.MessageCount(player, NetStats.Category.Correction)}", _label);
            GUILayout.Label($"entities known {Game.Replica.EntityCount}", _label);
            GUILayout.EndArea();
        }

        private void DrawMatchResult()
        {
            if (!Game.Replica.MatchOver) return;

            var rect = new Rect(Screen.width / 2 - 130, Screen.height / 2 - 40, 260, 80);
            GUILayout.BeginArea(rect, _panel);
            GUILayout.Label(Game.Replica.LocalPlayerWon ? "VICTORY" : "DEFEAT", _heading);
            GUILayout.Label(Game.Replica.LocalPlayerWon
                ? "The rival hold has struck its colours."
                : "Your hold has fallen.", _label);
            GUILayout.EndArea();
        }
    }
}
