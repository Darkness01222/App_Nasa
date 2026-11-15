using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public enum BarType { Energia, Radiacion, SoporteVital }

/// <summary>
/// Barra de estado (0-100). Se construye por codigo con la estructura:
///
///   StatusBar (64x32 px * uiScale)
///    |- Slider (ocupa EXACTAMENTE el interior del contenedor del sprite)
///    |   |- Fill  (color dinamico)
///    |- Frame (el sprite de la barra, dibujado encima; su interior es transparente)
///
/// Medidas tomadas de los sprites (64x32): interior del contenedor = x 21..60, y 11..21.
/// </summary>
public class StatusBar : MonoBehaviour
{
    // Medidas del sprite en pixeles (origen arriba-izquierda)
    private const float SpriteW = 64f, SpriteH = 32f;
    private const float InnerLeft = 21f, InnerRight = 61f, InnerTop = 11f, InnerBottom = 22f;

    [Header("Valores")]
    public BarType type;
    public float maxValue = 100f;
    public float startValue = 100f;

    [Header("Descenso automatico")]
    public bool decayEnabled = true;
    [Tooltip("Cuanto baja en cada intervalo")]
    public float decayAmount = 1f;
    [Tooltip("Segundos entre cada descenso")]
    public float decayInterval = 2f;

    [Header("Colores")]
    public Color fullColor = new Color(0.20f, 0.80f, 0.20f);   // verde (100%)
    public Color midColor  = new Color(1.00f, 0.85f, 0.10f);   // amarillo (50%)
    public Color lowColor  = new Color(0.90f, 0.15f, 0.15f);   // rojo (20%)
    public float midThreshold = 50f;
    public float lowThreshold = 20f;
    public Color emptyColor = new Color(0.10f, 0.10f, 0.12f);  // fondo de la barra vacia

    /// <summary>Se dispara cada vez que cambia el valor (barra, valor nuevo).</summary>
    public event Action<StatusBar, float> OnValueChanged;
    /// <summary>Se dispara cuando la barra llega a 0.</summary>
    public event Action<StatusBar> OnDepleted;

    private float _value;
    private float _timer;
    private bool _initialized;
    private bool _depletedNotified;
    private Slider _slider;
    private Image _fill;
    private RectTransform _rt;
    private Coroutine _pulse;

    // ---------------------------------------------------------------------
    //  CONSTRUCCION
    // ---------------------------------------------------------------------

    public void Initialize(BarType barType, Sprite frameSprite, float uiScale)
    {
        type = barType;
        _rt = GetComponent<RectTransform>();
        _rt.sizeDelta = new Vector2(SpriteW, SpriteH) * uiScale;

        // --- Slider: ocupa solo el interior del contenedor del sprite ---
        var container = new GameObject("Slider", typeof(RectTransform), typeof(Image), typeof(Slider));
        var cRt = container.GetComponent<RectTransform>();
        cRt.SetParent(_rt, false);
        cRt.anchorMin = new Vector2(InnerLeft / SpriteW, 1f - InnerBottom / SpriteH);
        cRt.anchorMax = new Vector2(InnerRight / SpriteW, 1f - InnerTop / SpriteH);
        cRt.offsetMin = cRt.offsetMax = Vector2.zero;

        var bg = container.GetComponent<Image>();
        bg.color = emptyColor;
        bg.raycastTarget = false;

        // --- Fill ---
        var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        var fRt = fillGo.GetComponent<RectTransform>();
        fRt.SetParent(cRt, false);
        fRt.anchorMin = Vector2.zero;
        fRt.anchorMax = Vector2.one;
        fRt.offsetMin = fRt.offsetMax = Vector2.zero;
        _fill = fillGo.GetComponent<Image>();
        _fill.raycastTarget = false;

        // --- Configuracion del Slider (no interactuable) ---
        _value = Mathf.Clamp(startValue, 0f, maxValue);
        _slider = container.GetComponent<Slider>();
        _slider.fillRect = fRt;
        _slider.handleRect = null;
        _slider.targetGraphic = null;
        _slider.direction = Slider.Direction.LeftToRight;
        _slider.minValue = 0f;
        _slider.maxValue = maxValue;
        _slider.wholeNumbers = false;
        _slider.transition = Selectable.Transition.None;
        _slider.navigation = new Navigation { mode = Navigation.Mode.None };
        _slider.interactable = false;
        _slider.value = _value;

        // --- Marco (sprite) encima de todo ---
        var frameGo = new GameObject("Frame", typeof(RectTransform), typeof(Image));
        var frRt = frameGo.GetComponent<RectTransform>();
        frRt.SetParent(_rt, false);
        frRt.anchorMin = Vector2.zero;
        frRt.anchorMax = Vector2.one;
        frRt.offsetMin = frRt.offsetMax = Vector2.zero;
        var frame = frameGo.GetComponent<Image>();
        frame.sprite = frameSprite;
        frame.preserveAspect = false;
        frame.raycastTarget = false;

        UpdateColor();
        _initialized = true;
    }

    // ---------------------------------------------------------------------
    //  LOGICA
    // ---------------------------------------------------------------------

    private void Update()
    {
        if (!_initialized || !decayEnabled || _value <= 0f) return;

        _timer += Time.deltaTime;
        while (_timer >= decayInterval)
        {
            _timer -= decayInterval;
            SetValue(_value - decayAmount);
        }
    }

    // ---------------------------------------------------------------------
    //  API PUBLICA
    // ---------------------------------------------------------------------

    /// <summary>Porcentaje actual redondeado (0-100). Ej: 89</summary>
    public int GetPorcentage()
    {
        return Mathf.RoundToInt(_value / maxValue * 100f);
    }

    /// <summary>Porcentaje actual con decimales.</summary>
    public float GetPorcentageExact()
    {
        return _value / maxValue * 100f;
    }

    /// <summary>Valor absoluto actual (0-maxValue).</summary>
    public float GetValue()
    {
        return _value;
    }

    public void SetValue(float newValue)
    {
        newValue = Mathf.Clamp(newValue, 0f, maxValue);
        if (Mathf.Approximately(newValue, _value)) return;

        _value = newValue;
        if (_slider != null) _slider.value = _value;
        UpdateColor();

        OnValueChanged?.Invoke(this, _value);

        if (_value <= 0f)
        {
            if (!_depletedNotified)
            {
                _depletedNotified = true;
                OnDepleted?.Invoke(this);
            }
        }
        else _depletedNotified = false;
    }

    /// <summary>Suma al valor (con efecto visual). Devuelve lo que realmente se sumo.</summary>
    public float Add(float amount)
    {
        float before = _value;
        SetValue(_value + amount);
        float applied = _value - before;
        if (applied > 0f) Pulse();
        return applied;
    }

    public void Subtract(float amount)
    {
        SetValue(_value - amount);
    }

    /// <summary>Pequeno "latido" de la barra para indicar que se repuso.</summary>
    public void Pulse()
    {
        if (!isActiveAndEnabled) return;
        if (_pulse != null) StopCoroutine(_pulse);
        _pulse = StartCoroutine(PulseRoutine());
    }

    // ---------------------------------------------------------------------

    private void UpdateColor()
    {
        if (_fill == null) return;
        _fill.color = GetColorFor(GetPorcentageExact());
    }

    private Color GetColorFor(float p)
    {
        if (p >= midThreshold)
            return Color.Lerp(midColor, fullColor, Mathf.InverseLerp(midThreshold, 100f, p));
        if (p > lowThreshold)
            return Color.Lerp(lowColor, midColor, Mathf.InverseLerp(lowThreshold, midThreshold, p));
        return lowColor;
    }

    private IEnumerator PulseRoutine()
    {
        const float duration = 0.3f;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float s = 1f + 0.2f * Mathf.Sin(Mathf.Clamp01(t / duration) * Mathf.PI);
            _rt.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        _rt.localScale = Vector3.one;
        _pulse = null;
    }
}
