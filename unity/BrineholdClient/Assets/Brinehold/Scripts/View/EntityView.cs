using Brinehold.Net.Client;
using Brinehold.Sim.World;
using UnityEngine;

namespace Brinehold.Unity.Boot
{
    /// <summary>
    /// One visible thing.
    ///
    /// The view never decides anything. It is told a position by the replica twenty times a second
    /// and interpolates between the last two, so movement looks smooth at any frame rate without the
    /// renderer ever becoming a source of truth about where a unit is.
    /// </summary>
    public sealed class EntityView : MonoBehaviour
    {
        public EntityKind Kind;
        public Renderer TintedRenderer;
        public GameObject SelectionRing;
        public GameObject CarryIndicator;

        private Vector3 _previous;
        private Vector3 _target;
        private float _targetYaw;
        private float _previousYaw;
        private bool _selected;

        private static readonly int ColourProperty = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock _properties;

        public void Bind(ReplicaWorld.Entity entity, Color colour)
        {
            Vector3 position = ToWorld(entity);
            _previous = _target = position;
            transform.position = position;

            if (TintedRenderer != null)
            {
                _properties ??= new MaterialPropertyBlock();
                TintedRenderer.GetPropertyBlock(_properties);
                _properties.SetColor(ColourProperty, colour);
                TintedRenderer.SetPropertyBlock(_properties);
            }

            // Buildings and resource nodes scale with their footprint so the silhouette reads at
            // RTS camera distance without needing distinct art for the prototype.
            float scale = entity.Kind switch
            {
                EntityKind.Building => 1f + 2f * Brinehold.Sim.Content.PrototypeContent
                    .ForBuilding(entity.Building).FootprintHalf,
                EntityKind.ResourceNode => 1.2f,
                EntityKind.Ship => 2.0f,
                _ => 1f
            };
            transform.localScale = new Vector3(scale, scale, scale);

            SetSelected(false);
        }

        public void SetTarget(ReplicaWorld.Entity entity)
        {
            _previous = _target;
            _previousYaw = _targetYaw;
            _target = ToWorld(entity);
            _targetYaw = -(float)entity.State.Value.Heading.ToDouble() * Mathf.Rad2Deg + 90f;

            if (CarryIndicator != null)
            {
                bool carrying = entity.State.Value.Job == JobType.Delivering;
                if (CarryIndicator.activeSelf != carrying) CarryIndicator.SetActive(carrying);
            }
        }

        public void Interpolate(float alpha)
        {
            transform.position = Vector3.Lerp(_previous, _target, alpha);
            if (Kind == EntityKind.Worker || Kind == EntityKind.Soldier || Kind == EntityKind.Ship)
                transform.rotation = Quaternion.Euler(0f, Mathf.LerpAngle(_previousYaw, _targetYaw, alpha), 0f);
        }

        public void SetSelected(bool selected)
        {
            _selected = selected;
            if (SelectionRing != null && SelectionRing.activeSelf != selected) SelectionRing.SetActive(selected);
        }

        public bool IsSelected => _selected;

        private static Vector3 ToWorld(ReplicaWorld.Entity entity)
        {
            Brinehold.Core.Math.Fix2 p = entity.State.Value.Position;
            return new Vector3((float)p.X.ToDouble(), 0f, (float)p.Y.ToDouble());
        }
    }
}
