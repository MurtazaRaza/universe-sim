using UnityEngine;
using UnityEngine.UIElements;

namespace UniverseSim.Unity
{
    /// <summary>The shape of a body label's icon (A-10).</summary>
    public enum BodyIconShape
    {
        None,
        Star,
        Planet,
        DwarfPlanet,
        Moon,
    }

    /// <summary>
    /// A small icon beside a body's label for the kind of body (A-10, docs/ART_DIRECTION.md section 8): a disc with
    /// rays for a star, a disc for a planet, a ring for a dwarf planet and a crescent for a moon. It is drawn by code
    /// in the element's USS color (one class per shape), so it is no artistic asset. The HUD's USS shows it only while
    /// the labels layer has the "stylized" class (StylizedLabels).
    /// </summary>
    public sealed class BodyIcon : VisualElement
    {
        /// <summary>The USS class of every body icon.</summary>
        public const string UssClass = "body-icon";

        public BodyIcon(BodyIconShape shape)
        {
            Shape = shape;
            pickingMode = PickingMode.Ignore;
            AddToClassList(UssClass);
            AddToClassList(ClassFor(shape));
            generateVisualContent += Draw;
        }

        /// <summary>The icon's shape.</summary>
        public BodyIconShape Shape { get; }

        /// <summary>The icon for a body of kind <paramref name="bodyKind"/> (usim::catalog::BodyKind).</summary>
        public static BodyIconShape ShapeFor(int bodyKind) => bodyKind switch
        {
            1 => BodyIconShape.Star,
            2 => BodyIconShape.Planet,
            3 => BodyIconShape.DwarfPlanet,
            4 => BodyIconShape.Moon,
            _ => BodyIconShape.None,
        };

        /// <summary>The USS class that colors icons of <paramref name="shape"/>, for example "body-icon--moon".</summary>
        public static string ClassFor(BodyIconShape shape) => UssClass + "--" + shape.ToString().ToLowerInvariant();

        private void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            var r = 0.5f * Mathf.Min(rect.width, rect.height);
            if (r <= 0f)
            {
                return;
            }
            var c = rect.center;
            var p = context.painter2D;
            var color = resolvedStyle.color;
            p.fillColor = color;
            p.strokeColor = color;
            switch (Shape)
            {
                case BodyIconShape.Star:
                    Disc(p, c, 0.42f * r);
                    p.lineWidth = Mathf.Max(1f, 0.2f * r);
                    p.lineCap = LineCap.Round;
                    p.BeginPath();
                    for (var k = 0; k < 8; ++k)
                    {
                        var a = k * Mathf.PI / 4f;
                        var direction = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        p.MoveTo(c + direction * (0.62f * r));
                        p.LineTo(c + direction * (0.98f * r));
                    }
                    p.Stroke();
                    break;
                case BodyIconShape.Planet:
                    Disc(p, c, 0.78f * r);
                    break;
                case BodyIconShape.DwarfPlanet:
                    p.lineWidth = Mathf.Max(1.2f, 0.24f * r);
                    p.BeginPath();
                    p.Arc(c, 0.62f * r, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.ClosePath();
                    p.Stroke();
                    break;
                case BodyIconShape.Moon:
                    Crescent(p, c, 0.8f * r);
                    break;
            }
        }

        private static void Disc(Painter2D p, Vector2 center, float radius)
        {
            p.BeginPath();
            p.Arc(center, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill();
        }

        // A crescent opening to the right: the left part of a disc not covered by an equal disc moved right by
        // `offset` of the radius. With y down, a clockwise arc runs toward increasing angles.
        private static void Crescent(Painter2D p, Vector2 center, float radius, float offset = 0.55f)
        {
            var dx = offset * radius;
            var h = Mathf.Sqrt(radius * radius - 0.25f * dx * dx);
            var theta = Mathf.Atan2(h, 0.5f * dx) * Mathf.Rad2Deg; // the lower meeting point, from the outer center
            p.BeginPath();
            // From the upper meeting point around the left of the outer disc to the lower one...
            p.Arc(center, radius, Angle.Degrees(-theta), Angle.Degrees(theta), ArcDirection.CounterClockwise);
            // ...and back along the left of the inner disc.
            p.Arc(center + new Vector2(dx, 0f), radius, Angle.Degrees(180f - theta), Angle.Degrees(180f + theta),
                  ArcDirection.Clockwise);
            p.ClosePath();
            p.Fill();
        }
    }
}
