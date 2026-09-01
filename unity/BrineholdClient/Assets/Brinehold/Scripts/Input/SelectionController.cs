using System.Collections.Generic;
using Brinehold.Core.Collections;
using Brinehold.Core.Math;
using Brinehold.Sim.Commands;
using Brinehold.Sim.World;
using UnityEngine;

namespace Brinehold.Unity.Boot
{
    /// <summary>
    /// Mouse and keyboard into orders.
    ///
    /// Like the camera, all the actual rules live in tested engine-independent code: SelectionModel
    /// decides what a box catches, OrderIssuer decides what a right-click means, ControlGroups
    /// handles the number keys. This class is the adapter that turns Unity input into those calls
    /// and paints the drag rectangle.
    /// </summary>
    public sealed class SelectionController : MonoBehaviour
    {
        public GameBootstrap Game;
        public CameraController CameraRig;
        public EntityViewPool Views;

        [Header("Feel")]
        [Tooltip("Pixels the mouse must travel before a click becomes a drag.")]
        public float DragThreshold = 6f;
        public float DoubleClickSeconds = 0.35f;
        public Color BoxFill = new Color(0.4f, 0.9f, 0.5f, 0.15f);
        public Color BoxEdge = new Color(0.5f, 1f, 0.6f, 0.9f);

        private Vector2 _dragStartScreen;
        private Fix2 _dragStartWorld;
        private bool _dragging;
        private float _lastClickTime;
        private EntityId _lastClicked;
        private EntityId _lastIdleWorker;
        private Texture2D _boxTexture;

        private void Update()
        {
            if (Game == null || CameraRig == null) return;

            HandlePlacement();
            HandleLeftMouse();
            HandleRightMouse();
            HandleControlGroups();
            HandleHotkeys();

            if (Views != null)
            {
                Views.Interpolate(Game.TickAlpha);
                RefreshSelectionRings();
            }
        }

        // ------------------------------------------------------------------ left mouse

        private void HandleLeftMouse()
        {
            if (Game.Placement.Active) return;   // placement consumes the left button

            if (UnityEngine.Input.GetMouseButtonDown(0))
            {
                _dragStartScreen = UnityEngine.Input.mousePosition;
                if (CameraRig.TryGetGroundPoint(out Fix2 world)) _dragStartWorld = world;
                _dragging = false;
            }

            if (UnityEngine.Input.GetMouseButton(0))
            {
                if (!_dragging && Vector2.Distance(_dragStartScreen, UnityEngine.Input.mousePosition) > DragThreshold)
                    _dragging = true;
            }

            if (!UnityEngine.Input.GetMouseButtonUp(0)) return;

            bool additive = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);

            if (_dragging)
            {
                if (CameraRig.TryGetGroundPoint(out Fix2 end))
                {
                    List<EntityId> boxed = Game.Selection.BoxSelect(_dragStartWorld, end);
                    if (additive) Game.Selection.AddMany(boxed);
                    else Game.Selection.SetMany(boxed);
                }
                _dragging = false;
                return;
            }

            if (!CameraRig.TryGetGroundPoint(out Fix2 point)) return;
            EntityId picked = Game.Selection.Pick(point, Fix64.FromFraction(12, 10));

            bool doubleClick = !picked.IsNone && picked == _lastClicked
                               && Time.time - _lastClickTime < DoubleClickSeconds;
            _lastClicked = picked;
            _lastClickTime = Time.time;

            if (doubleClick)
            {
                // Select everything of the same kind currently on screen.
                Game.Rig.VisibleRegion(out Fix2 min, out Fix2 max);
                Game.Selection.SetMany(Game.Selection.SelectSameKindInRegion(picked, min, max));
                return;
            }

            if (picked.IsNone) { if (!additive) Game.Selection.Clear(); return; }
            if (additive) Game.Selection.Toggle(picked);
            else Game.Selection.Set(picked);
        }

        // ------------------------------------------------------------------ right mouse

        private void HandleRightMouse()
        {
            if (!UnityEngine.Input.GetMouseButtonDown(1)) return;

            if (Game.Placement.Active) { Game.Placement.Cancel(); return; }
            if (!CameraRig.TryGetGroundPoint(out Fix2 point)) return;

            Command order = Game.Orders.RightClick(point, Game.ClientNav);
            if (order != null) Game.Issue(order);
        }

        // ------------------------------------------------------------------ placement

        private void HandlePlacement()
        {
            if (!Game.Placement.Active) return;

            if (CameraRig.TryGetGroundPoint(out Fix2 point)) Game.Placement.MoveTo(point);

            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) { Game.Placement.Cancel(); return; }

            if (!UnityEngine.Input.GetMouseButtonDown(0)) return;
            if (!Game.Placement.Legal) return;

            Command order = Game.Orders.PlaceBuilding(
                Game.Placement.Type, Game.Placement.CellX, Game.Placement.CellY);
            if (order != null) Game.Issue(order);

            // Shift keeps the ghost up for placing a row of buildings.
            if (!UnityEngine.Input.GetKey(KeyCode.LeftShift)) Game.Placement.Cancel();
        }

        // ------------------------------------------------------------------ keys

        private void HandleControlGroups()
        {
            for (int i = 0; i <= 9; i++)
            {
                KeyCode key = KeyCode.Alpha0 + i;
                if (!UnityEngine.Input.GetKeyDown(key)) continue;

                bool control = UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl);
                bool shift = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);

                if (control) Game.Groups.Assign(i, Game.Selection.Selected);
                else if (shift) Game.Groups.Append(i, Game.Selection.Selected);
                else Game.Selection.SetMany(Game.Groups.Recall(i));
            }
        }

        private void HandleHotkeys()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.H))
            {
                _lastIdleWorker = Game.Selection.NextIdleWorker(_lastIdleWorker);
                if (!_lastIdleWorker.IsNone)
                {
                    Game.Selection.Set(_lastIdleWorker);
                    if (Game.Replica.TryGet(_lastIdleWorker, out var entity))
                        Game.Rig.JumpTo(entity.State.Value.Position);
                }
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.Space) && Game.Selection.Count > 0)
            {
                foreach (EntityId id in Game.Selection.Selected)
                    if (Game.Replica.TryGet(id, out var entity))
                    {
                        Game.Rig.JumpTo(entity.State.Value.Position);
                        break;
                    }
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.X))
            {
                Command order = Game.Orders.Stop();
                if (order != null) Game.Issue(order);
            }

            // Build hotkeys, laid out so the positions are learnable.
            if (UnityEngine.Input.GetKeyDown(KeyCode.B)) Game.Placement.Begin(BuildingType.House);
            if (UnityEngine.Input.GetKeyDown(KeyCode.N)) Game.Placement.Begin(BuildingType.LumberCamp);
            if (UnityEngine.Input.GetKeyDown(KeyCode.M)) Game.Placement.Begin(BuildingType.FishingWharf);
            if (UnityEngine.Input.GetKeyDown(KeyCode.K)) Game.Placement.Begin(BuildingType.Dock);

            // Training from a selected building.
            if (UnityEngine.Input.GetKeyDown(KeyCode.V))
            {
                Command order = Game.Orders.Train(EntityKind.Worker);
                if (order != null) Game.Issue(order);
            }
            if (UnityEngine.Input.GetKeyDown(KeyCode.C))
            {
                Command order = Game.Orders.Train(EntityKind.Soldier);
                if (order != null) Game.Issue(order);
            }
            if (UnityEngine.Input.GetKeyDown(KeyCode.F))
            {
                Command order = Game.Orders.Train(EntityKind.Ship);
                if (order != null) Game.Issue(order);
            }
        }

        private void RefreshSelectionRings()
        {
            foreach (var entity in Game.Replica.Entities)
                if (Views.TryGetView(entity.Id.Raw, out EntityView view))
                    view.SetSelected(Game.Selection.Contains(entity.Id));
        }

        // ------------------------------------------------------------------ drag rectangle

        private void OnGUI()
        {
            if (!_dragging) return;

            _boxTexture ??= Texture2D.whiteTexture;

            Vector2 current = UnityEngine.Input.mousePosition;
            float x = Mathf.Min(_dragStartScreen.x, current.x);
            float y = Screen.height - Mathf.Max(_dragStartScreen.y, current.y);
            float width = Mathf.Abs(current.x - _dragStartScreen.x);
            float height = Mathf.Abs(current.y - _dragStartScreen.y);
            var rect = new Rect(x, y, width, height);

            Color previous = GUI.color;
            GUI.color = BoxFill;
            GUI.DrawTexture(rect, _boxTexture);
            GUI.color = BoxEdge;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1), _boxTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1, rect.width, 1), _boxTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 1, rect.height), _boxTexture);
            GUI.DrawTexture(new Rect(rect.xMax - 1, rect.y, 1, rect.height), _boxTexture);
            GUI.color = previous;
        }
    }
}
