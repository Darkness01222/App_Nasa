using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Crea por codigo el Canvas y las 3 barras (Energia, Radiacion, Soporte vital)
/// alineadas una tras otra en la parte baja de la pantalla.
///
/// USO: agrega este componente a un GameObject vacio de la escena y arrastra
/// los 3 sprites. No necesitas crear Canvas ni nada mas.
///
/// Ejemplos:
///   StatusBarsUI.Instance.GetBar(BarType.Energia).GetPorcentage();   // 89
///   StatusBarsUI.Instance.GetPorcentage(BarType.Radiacion);          // 100
/// </summary>
public class StatusBarsUI : MonoBehaviour
{
    public static StatusBarsUI Instance { get; private set; }

    [Header("Sprites de las barras (64x32)")]
    public Sprite energiaSprite;
    public Sprite radiacionSprite;
    public Sprite soporteVitalSprite;

    [Header("Layout")]
    [Tooltip("Multiplicador del tamano del sprite (64x32 px). Usa enteros para que se vea nitido")]
    public float uiScale = 3f;
    public float spacing = 16f;
    public float bottomMargin = 16f;
    public Vector2 referenceResolution = new Vector2(1280f, 720f);

    [Header("Canvas (opcional)")]
    [Tooltip("Si lo dejas vacio se crea uno nuevo automaticamente")]
    public Canvas targetCanvas;

    [Header("Valores de todas las barras")]
    public float startValue = 100f;
    public float decayAmount = 1f;
    public float decayInterval = 2f;

    private readonly Dictionary<BarType, StatusBar> _bars = new Dictionary<BarType, StatusBar>();
    private readonly List<BarType> _order = new List<BarType>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Ya existe un StatusBarsUI en la escena. Se destruye este componente.", this);
            Destroy(this);
            return;
        }
        Instance = this;
        BuildUI();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ---------------------------------------------------------------------
    //  CONSTRUCCION DE LA UI
    // ---------------------------------------------------------------------

    private void BuildUI()
    {
        Canvas canvas = targetCanvas;
        if (canvas == null)
        {
            var cgo = new GameObject("Status Bars Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            cgo.transform.SetParent(transform, false);
            canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = cgo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        // Contenedor en la parte baja, centrado, con las barras una tras otra
        var containerGo = new GameObject("Bars Container",
            typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        var cRt = containerGo.GetComponent<RectTransform>();
        cRt.SetParent(canvas.transform, false);
        cRt.anchorMin = cRt.anchorMax = cRt.pivot = new Vector2(0.5f, 0f);
        cRt.anchoredPosition = new Vector2(0f, bottomMargin);

        var layout = containerGo.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var fitter = containerGo.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        CreateBar(BarType.Energia, energiaSprite, cRt);
        CreateBar(BarType.Radiacion, radiacionSprite, cRt);
        CreateBar(BarType.SoporteVital, soporteVitalSprite, cRt);
    }

    private void CreateBar(BarType type, Sprite sprite, RectTransform parent)
    {
        if (sprite == null)
            Debug.LogWarning("StatusBarsUI: falta el sprite de " + type, this);

        var go = new GameObject("Bar " + type, typeof(RectTransform), typeof(StatusBar));
        go.GetComponent<RectTransform>().SetParent(parent, false);

        var bar = go.GetComponent<StatusBar>();
        bar.startValue = startValue;
        bar.maxValue = 100f;
        bar.decayAmount = decayAmount;
        bar.decayInterval = decayInterval;
        bar.Initialize(type, sprite, uiScale);

        _bars[type] = bar;
        _order.Add(type);
    }

    // ---------------------------------------------------------------------
    //  API PUBLICA
    // ---------------------------------------------------------------------

    public StatusBar GetBar(BarType type)
    {
        StatusBar bar;
        return _bars.TryGetValue(type, out bar) ? bar : null;
    }

    /// <summary>Atajo: porcentaje (0-100) de una barra.</summary>
    public int GetPorcentage(BarType type)
    {
        var bar = GetBar(type);
        return bar != null ? bar.GetPorcentage() : 0;
    }

    /// <summary>Sube una barra concreta (valor logico + visual).</summary>
    public float AddToBar(BarType type, float amount)
    {
        var bar = GetBar(type);
        return bar != null ? bar.Add(amount) : 0f;
    }

    /// <summary>Sube una barra elegida al azar. Devuelve cual fue.</summary>
    public BarType AddToRandomBar(float amount)
    {
        BarType type = _order[Random.Range(0, _order.Count)];
        AddToBar(type, amount);
        return type;
    }

    /// <summary>Color representativo de cada barra (para efectos).</summary>
    public Color GetAccentColor(BarType type)
    {
        switch (type)
        {
            case BarType.Energia:      return new Color(0.35f, 0.65f, 1.00f);
            case BarType.Radiacion:    return new Color(1.00f, 0.90f, 0.20f);
            default:                   return new Color(0.40f, 0.95f, 0.45f);
        }
    }
}
