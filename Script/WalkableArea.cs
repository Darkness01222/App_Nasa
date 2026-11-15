using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Define el area caminable de un escenario: un perimetro (los bordes de la sala)
/// y una lista de obstaculos rectangulares (como la pared del centro).
///
/// - Genera colliders reales (EdgeCollider2D + BoxCollider2D) para que nada atraviese las paredes.
/// - Ofrece consultas matematicas (IsWalkable / IsPathClear) para que los personajes
///   decidan a donde ir sin usar fisica (rapido y estable en WebGL).
///
/// USO: colocar este componente en un GameObject con la MISMA posicion que el sprite del
/// escenario (Base.png), con escala (1,1,1). Los valores por defecto estan medidos
/// sobre Base.png (800x600 px). Ajustalos viendo los Gizmos en la ventana Scene.
/// </summary>
public class WalkableArea : MonoBehaviour
{
    [System.Serializable]
    public class PixelRect
    {
        public string name = "Obstaculo";
        [Tooltip("Centro en pixeles de la imagen (origen arriba-izquierda, Y hacia abajo)")]
        public Vector2 centerPx;
        [Tooltip("Tamano en pixeles")]
        public Vector2 sizePx;
        [Tooltip("Si se desactiva, se ignora")]
        public bool enabled = true;
    }

    [Header("Conversion de la imagen del escenario")]
    [Tooltip("Tamano en pixeles de la imagen del escenario")]
    public Vector2 imageSizePixels = new Vector2(800f, 600f);
    [Tooltip("Pixels Per Unit con el que importaste el sprite del escenario")]
    public float pixelsPerUnit = 100f;

    [Header("Perimetro de la sala (pixeles de la imagen, sentido horario)")]
    public List<Vector2> boundaryPixels = new List<Vector2>
    {
        new Vector2(127,  95), new Vector2(640,  95), new Vector2(640, 190),
        new Vector2(728, 190), new Vector2(728, 415), new Vector2(665, 415),
        new Vector2(665, 510), new Vector2(127, 510), new Vector2(127, 415),
        new Vector2( 63, 415), new Vector2( 63, 190), new Vector2(127, 190)
    };

    [Header("Obstaculos (pixeles de la imagen)")]
    public List<PixelRect> obstacles = new List<PixelRect>
    {
        // Pared central (la importante)
        new PixelRect { name = "Pared central",     centerPx = new Vector2(384, 296), sizePx = new Vector2(256, 68) },
        // Muebles (opcionales: desactivalos o borralos si no los quieres)
        new PixelRect { name = "Mostrador inferior", centerPx = new Vector2(313, 430), sizePx = new Vector2(178, 26) },
        new PixelRect { name = "Lockers superiores", centerPx = new Vector2(193, 97),  sizePx = new Vector2(95, 46) },
        new PixelRect { name = "Cocina derecha",     centerPx = new Vector2(680, 193), sizePx = new Vector2(98, 32) },
        new PixelRect { name = "Mesa con sillas",    centerPx = new Vector2(137, 297), sizePx = new Vector2(98, 56) },
        new PixelRect { name = "Consola central",    centerPx = new Vector2(392, 148), sizePx = new Vector2(66, 56) },
    };

    [Header("Colliders fisicos")]
    public bool generateColliders = true;

    [Header("Gizmos")]
    public bool drawGizmos = true;

    // Datos en espacio local (unidades de Unity)
    private Vector2[] _poly;
    private Rect[] _rects;

    private void Awake()
    {
        Rebuild();
    }

    /// <summary>Recalcula los datos y regenera los colliders.</summary>
    public void Rebuild()
    {
        _poly = new Vector2[boundaryPixels.Count];
        for (int i = 0; i < _poly.Length; i++)
            _poly[i] = PxToLocal(boundaryPixels[i]);

        var rectList = new List<Rect>();
        foreach (var o in obstacles)
        {
            if (!o.enabled) continue;
            Vector2 c = PxToLocal(o.centerPx);
            Vector2 s = o.sizePx / pixelsPerUnit;
            rectList.Add(new Rect(c - s * 0.5f, s));
        }
        _rects = rectList.ToArray();

        if (generateColliders && Application.isPlaying) BuildColliders();
    }

    private Vector2 PxToLocal(Vector2 px)
    {
        return new Vector2(
            (px.x - imageSizePixels.x * 0.5f) / pixelsPerUnit,
            -(px.y - imageSizePixels.y * 0.5f) / pixelsPerUnit);
    }

    private void BuildColliders()
    {
        // Limites de la sala
        var boundary = new GameObject("Boundary Collider");
        boundary.transform.SetParent(transform, false);
        var edge = boundary.AddComponent<EdgeCollider2D>();
        var pts = new Vector2[_poly.Length + 1];
        for (int i = 0; i < _poly.Length; i++) pts[i] = _poly[i];
        pts[_poly.Length] = _poly[0]; // cerrar el perimetro
        edge.points = pts;

        // Obstaculos (incluye la pared del centro)
        for (int i = 0; i < _rects.Length; i++)
        {
            var go = new GameObject("Obstacle " + i);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = _rects[i].center;
            var box = go.AddComponent<BoxCollider2D>();
            box.size = _rects[i].size;
        }
    }

    // ---------------------------------------------------------------------
    //  CONSULTAS
    // ---------------------------------------------------------------------

    /// <summary>
    /// true si un circulo de 'radius' (unidades de mundo) centrado en worldPoint
    /// esta dentro de la sala y sin tocar ningun obstaculo.
    /// </summary>
    public bool IsWalkable(Vector2 worldPoint, float radius = 0.15f)
    {
        if (_poly == null) Rebuild();

        Vector2 p = transform.InverseTransformPoint(worldPoint);
        float r = radius / Mathf.Abs(transform.lossyScale.x);

        if (!PointInPolygon(p, _poly)) return false;
        if (DistanceToPolygonEdges(p, _poly) < r) return false;

        for (int i = 0; i < _rects.Length; i++)
            if (DistanceToRect(p, _rects[i]) < r) return false;

        return true;
    }

    /// <summary>true si el camino recto entre a y b no sale del area ni cruza obstaculos.</summary>
    public bool IsPathClear(Vector2 worldA, Vector2 worldB, float radius = 0.15f)
    {
        float dist = Vector2.Distance(worldA, worldB);
        int steps = Mathf.Max(1, Mathf.CeilToInt(dist / 0.08f));
        for (int i = 1; i <= steps; i++)
        {
            Vector2 p = Vector2.Lerp(worldA, worldB, i / (float)steps);
            if (!IsWalkable(p, radius)) return false;
        }
        return true;
    }

    // ---------------------------------------------------------------------
    //  GEOMETRIA
    // ---------------------------------------------------------------------

    private static bool PointInPolygon(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
        }
        return inside;
    }

    private static float DistanceToPolygonEdges(Vector2 p, Vector2[] poly)
    {
        float min = float.MaxValue;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            min = Mathf.Min(min, DistanceToSegment(p, poly[j], poly[i]));
        return min;
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector2.Distance(p, a + ab * t);
    }

    private static float DistanceToRect(Vector2 p, Rect r)
    {
        float dx = Mathf.Max(Mathf.Abs(p.x - r.center.x) - r.width * 0.5f, 0f);
        float dy = Mathf.Max(Mathf.Abs(p.y - r.center.y) - r.height * 0.5f, 0f);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    // ---------------------------------------------------------------------
    //  GIZMOS (ventana Scene)
    // ---------------------------------------------------------------------

    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        Gizmos.color = Color.green;
        for (int i = 0; i < boundaryPixels.Count; i++)
        {
            Vector3 a = transform.TransformPoint(PxToLocal(boundaryPixels[i]));
            Vector3 b = transform.TransformPoint(PxToLocal(boundaryPixels[(i + 1) % boundaryPixels.Count]));
            Gizmos.DrawLine(a, b);
        }

        Gizmos.color = Color.red;
        foreach (var o in obstacles)
        {
            if (!o.enabled) continue;
            Vector3 c = transform.TransformPoint(PxToLocal(o.centerPx));
            Vector3 s = (Vector3)(o.sizePx / pixelsPerUnit);
            s.Scale(transform.lossyScale);
            Gizmos.DrawWireCube(c, s);
        }
    }
}
