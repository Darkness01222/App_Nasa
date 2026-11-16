using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Gestiona los eventos: guarda la lista, construye por codigo toda la UI
/// (fondo negro, imagen con transicion de cuadros, texto con maquina de escribir,
/// 2 botones con flechas) y aplica los efectos sobre las barras de StatusBarsUI.
///
/// USO: agregalo a un GameObject vacio, llena la lista 'events' y arrastra
/// el objeto que contiene StatusBarsUI en 'barsUIRoot' (si lo dejas vacio usa
/// StatusBarsUI.Instance.gameObject).
/// </summary>
public class EventManager : MonoBehaviour
{
    public static EventManager Instance { get; private set; }

    /// <summary>True mientras corre un evento o el efecto de "evento ignorado".</summary>
    public bool IsBusy { get; private set; }

    [Header("Eventos")]
    public List<GameEvent> events = new List<GameEvent>();
    public bool avoidRepeatingLast = true;

    [Header("Referencias")]
    [Tooltip("Objeto que contiene el StatusBarsUI. Se oculta durante el evento")]
    public GameObject barsUIRoot;
    [Tooltip("Opcional. Si lo dejas vacio usa la fuente por defecto de Unity")]
    public Font font;
    public Vector2 referenceResolution = new Vector2(1280f, 720f);

    [Header("Transicion de la imagen (a cuadros)")]
    public int tileColumns = 16;
    public int tileRows = 9;
    public float transitionDuration = 1.2f;
    public float blackFadeTime = 0.35f;

    [Header("Tiempos")]
    [Tooltip("Segundos entre que termina la imagen y aparece el texto")]
    public float textDelay = 1f;
    public float defaultCharsPerSecond = 35f;
    public float buttonPopTime = 0.25f;
    public float closeFadeTime = 0.35f;

    [Header("Sonidos")]
    public AudioClip typeBlip;
    public int blipEveryNChars = 2;
    [Range(0f, 1f)] public float blipVolume = 0.3f;

    [Header("Estilo - Texto")]
    public int textFontSize = 34;
    public Color textPanelColor = new Color(0f, 0f, 0f, 0.78f);
    public Vector2 textPanelMin = new Vector2(0.08f, 0.30f);
    public Vector2 textPanelMax = new Vector2(0.92f, 0.52f);

    [Header("Estilo - Botones")]
    public Vector2 buttonsAreaMin = new Vector2(0.08f, 0.05f);
    public Vector2 buttonsAreaMax = new Vector2(0.92f, 0.27f);
    public float buttonSpacing = 40f;
    public int buttonLabelSize = 30;
    public Color buttonColor = new Color(0.16f, 0.18f, 0.28f, 0.95f);
    public Color buttonHoverColor = new Color(0.28f, 0.32f, 0.50f, 1f);
    public Color buttonPressedColor = new Color(0.10f, 0.11f, 0.18f, 1f);

    [Header("Estilo - Flechas")]
    public Color arrowUpColor = new Color(0.25f, 0.9f, 0.3f);
    public Color arrowDownColor = new Color(0.95f, 0.2f, 0.2f);
    public float arrowSize = 34f;
    [Tooltip("Desde esta cantidad (valor absoluto) se muestran 2 flechas")]
    public float twoArrowsFrom = 15f;
    [Tooltip("Desde esta cantidad se muestran 3 flechas")]
    public float threeArrowsFrom = 30f;
    [Tooltip("Cuadrito de color que indica a que barra afecta (azul/amarillo/verde). Blanco = barra al azar")]
    public bool showBarColorChip = true;
    public float chipSize = 22f;
    public float effectSpacing = 26f;

    [Header("Evento ignorado")]
    public string ignoredMessage = "¡NO ATENDISTE EL EVENTO!";
    public float ignoredPenalty = 15f;
    [Tooltip("true = baja todas las barras. false = baja una al azar")]
    public bool ignoredAffectsAllBars = true;
    public float ignoredDuration = 1.8f;
    public Color ignoredFlashColor = new Color(1f, 0f, 0f, 0.6f);
    public int ignoredFontSize = 64;
    public AudioClip ignoredSound;
    public bool shakeCamera = true;
    public float shakeMagnitude = 0.25f;

    // ---------------------------------------------------------------------

    private class ChoiceUI
    {
        public RectTransform rt;
        public Button button;
        public Text label;
        public RectTransform arrowRow;
    }

    private Canvas _canvas;
    private GameObject _eventRoot, _alertRoot;
    private CanvasGroup _rootGroup, _textGroup, _buttonsGroup;
    private Image _eventImage, _flash;
    private RectTransform[] _tileRts;
    private Text _mainText, _alertText;
    private readonly ChoiceUI[] _choices = new ChoiceUI[2];
    private Sprite _arrowSprite;
    private Font _font;
    private AudioSource _audio;       // efectos
    private AudioSource _narration;   // narracion del evento
    private int _lastIndex = -1;
    private int _chosen = -1;
    private bool _pausedByUs;
    private float _prevTimeScale = 1f;

    // ---------------------------------------------------------------------
    //  CICLO DE VIDA
    // ---------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Ya existe un EventManager en la escena. Se destruye este componente.", this);
            Destroy(this);
            return;
        }
        Instance = this;

        _font = GetFont();
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 0f;
        _narration = gameObject.AddComponent<AudioSource>();
        _narration.playOnAwake = false;
        _narration.loop = false;
        _narration.spatialBlend = 0f;

        EnsureEventSystem();
        _arrowSprite = BuildArrowSprite();
        BuildUI();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_pausedByUs) Time.timeScale = _prevTimeScale;   // por seguridad
    }

    // ---------------------------------------------------------------------
    //  API PUBLICA
    // ---------------------------------------------------------------------

    /// <summary>Elige un evento: indice fijo (>=0) o al azar (-1).</summary>
    public GameEvent PickEvent(int fixedIndex = -1)
    {
        if (events == null || events.Count == 0) return null;

        int idx;
        if (fixedIndex >= 0 && fixedIndex < events.Count)
        {
            idx = fixedIndex;
        }
        else if (avoidRepeatingLast && events.Count > 1 && _lastIndex >= 0)
        {
            idx = Random.Range(0, events.Count - 1);
            if (idx >= _lastIndex) idx++;
        }
        else
        {
            idx = Random.Range(0, events.Count);
        }

        _lastIndex = idx;
        return events[idx];
    }

    public bool StartEvent(GameEvent ev, System.Action onFinished = null)
    {
        if (IsBusy || ev == null) return false;
        IsBusy = true;
        StartCoroutine(EventRoutine(ev, onFinished));
        return true;
    }

    public bool HandleIgnored(System.Action onFinished = null)
    {
        if (IsBusy) return false;
        IsBusy = true;
        StartCoroutine(IgnoredRoutine(onFinished));
        return true;
    }

    // ---------------------------------------------------------------------
    //  FLUJO DEL EVENTO
    // ---------------------------------------------------------------------

    private IEnumerator EventRoutine(GameEvent ev, System.Action onFinished)
    {
        GameObject bars = GetBarsRoot();
        EventChoice applied = null;

        // try/finally: pase lo que pase (error, objeto destruido...) el juego se reanuda,
        // las barras vuelven y el manager queda libre. Asi el juego nunca se queda congelado.
        try
        {
            // 1) Preparar TODO antes de pausar (si algo falla aqui, el juego no se congela)
            Populate(ev);
            ResetTiles();
            _textGroup.alpha = 0f;
            _buttonsGroup.alpha = 0f;
            _buttonsGroup.interactable = false;
            _buttonsGroup.blocksRaycasts = false;
            foreach (var c in _choices) c.rt.localScale = Vector3.zero;
            _rootGroup.alpha = 0f;
            _eventRoot.SetActive(true);

            // 2) Ocultar barras, pausar y poner todo en negro
            if (bars != null) bars.SetActive(false);
            _prevTimeScale = Time.timeScale;
            _pausedByUs = true;
            Time.timeScale = 0f;
            yield return FadeGroup(_rootGroup, 0f, 1f, blackFadeTime);

            // 3) Imagen que se construye a cuadros
            yield return TileReveal();

            // 4) Espera y aparece el panel de texto
            yield return new WaitForSecondsRealtime(textDelay);
            yield return FadeGroup(_textGroup, 0f, 1f, 0.25f);

            // 5) Narracion + texto escribiendose AL MISMO TIEMPO
            float narrationLength = 0f;
            float narrationStart = Time.unscaledTime;
            if (ev.sound != null)
            {
                _narration.clip = ev.sound;
                _narration.volume = ev.soundVolume > 0f ? ev.soundVolume : 1f;
                _narration.Play();
                narrationLength = ev.sound.length;
            }
            yield return TypeText(ev, narrationLength);

            // Si la narracion dura mas que el texto, esperar a que termine
            while (narrationLength > 0f && Time.unscaledTime - narrationStart < narrationLength)
                yield return null;

            // 6) Botones de decision
            _chosen = -1;
            yield return ShowButtons();
            while (_chosen < 0) yield return null;
            applied = _chosen == 0 ? ev.choiceA : ev.choiceB;

            // 7) Cierre: vuelven las barras y se quita el negro
            _narration.Stop();
            if (bars != null) bars.SetActive(true);
            yield return FadeGroup(_rootGroup, 1f, 0f, closeFadeTime);
        }
        finally
        {
            if (_narration != null) _narration.Stop();
            if (_eventRoot != null) _eventRoot.SetActive(false);
            if (bars != null) bars.SetActive(true);
            if (_pausedByUs)
            {
                Time.timeScale = _prevTimeScale;
                _pausedByUs = false;
            }
            IsBusy = false;

            ApplyEffects(applied);   // con el juego ya corriendo para que se vea el pulso
            if (onFinished != null) onFinished();
        }
    }

    private void OnChoiceClicked(int index)
    {
        if (!IsBusy || _chosen >= 0) return;
        _chosen = index;
        _buttonsGroup.interactable = false;
    }

    private void ApplyEffects(EventChoice choice)
    {
        if (choice == null || choice.effects == null || StatusBarsUI.Instance == null) return;

        foreach (var e in choice.effects)
        {
            BarType type = e.randomBar ? RandomBarType() : e.bar;
            StatusBar bar = StatusBarsUI.Instance.GetBar(type);
            if (bar == null) continue;

            if (e.amount > 0f) bar.Add(e.amount);
            else if (e.amount < 0f) bar.Subtract(-e.amount);
        }
    }

    // ---------------------------------------------------------------------
    //  EVENTO IGNORADO
    // ---------------------------------------------------------------------

    private IEnumerator IgnoredRoutine(System.Action onFinished)
    {
        BarType[] allTypes = (BarType[])System.Enum.GetValues(typeof(BarType));
        Camera cam = shakeCamera ? Camera.main : null;
        Vector3 lastOffset = Vector3.zero;

        try
        {
            // Castigo
            if (StatusBarsUI.Instance != null)
            {
                if (ignoredAffectsAllBars)
                {
                    foreach (BarType tr in allTypes)
                    {
                        StatusBar b = StatusBarsUI.Instance.GetBar(tr);
                        if (b != null) b.Subtract(ignoredPenalty);
                    }
                }
                else
                {
                    StatusBar b = StatusBarsUI.Instance.GetBar(RandomBarType());
                    if (b != null) b.Subtract(ignoredPenalty);
                }
            }

            if (ignoredSound != null) _audio.PlayOneShot(ignoredSound);

            _alertText.text = ignoredMessage;
            _alertRoot.SetActive(true);

            RectTransform textRt = _alertText.rectTransform;
            Color fc = ignoredFlashColor;

            float t = 0f;
            while (t < ignoredDuration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / ignoredDuration);
                float fade = 1f - k;

                // Flash rojo parpadeante
                _flash.color = new Color(fc.r, fc.g, fc.b, fc.a * Mathf.Abs(Mathf.Sin(k * Mathf.PI * 3f)) * fade);

                // Texto: entra grande y de golpe, tiembla, y se desvanece al final
                float pk = Mathf.Clamp01(k / 0.15f);
                float pop = pk < 1f ? Mathf.Lerp(3f, 1f, 1f - Mathf.Pow(1f - pk, 3f)) : 1f + 0.05f * Mathf.Sin(t * 30f);
                textRt.localScale = Vector3.one * pop;
                textRt.localEulerAngles = new Vector3(0f, 0f, Mathf.Sin(t * 40f) * 3f * fade);
                Color tc = _alertText.color;
                tc.a = k > 0.75f ? 1f - (k - 0.75f) / 0.25f : 1f;
                _alertText.color = tc;

                // Temblor de camara
                if (cam != null)
                {
                    Vector3 off = (Vector3)(Random.insideUnitCircle * shakeMagnitude * fade);
                    cam.transform.position += off - lastOffset;
                    lastOffset = off;
                }

                // Temblor de las barras
                if (StatusBarsUI.Instance != null)
                {
                    foreach (BarType bt in allTypes)
                    {
                        StatusBar b = StatusBarsUI.Instance.GetBar(bt);
                        if (b != null) b.transform.localEulerAngles = new Vector3(0f, 0f, Random.Range(-1f, 1f) * 8f * fade);
                    }
                }

                yield return null;
            }
        }
        finally
        {
            // Limpieza (siempre se ejecuta)
            if (cam != null) cam.transform.position -= lastOffset;
            if (StatusBarsUI.Instance != null)
            {
                foreach (BarType bt in allTypes)
                {
                    StatusBar b = StatusBarsUI.Instance.GetBar(bt);
                    if (b != null) b.transform.localEulerAngles = Vector3.zero;
                }
            }
            if (_flash != null) _flash.color = Color.clear;
            if (_alertText != null)
            {
                _alertText.rectTransform.localScale = Vector3.one;
                _alertText.rectTransform.localEulerAngles = Vector3.zero;
                Color tc = _alertText.color; tc.a = 1f; _alertText.color = tc;
            }
            if (_alertRoot != null) _alertRoot.SetActive(false);

            IsBusy = false;
            if (onFinished != null) onFinished();
        }
    }

    // ---------------------------------------------------------------------
    //  ANIMACIONES
    // ---------------------------------------------------------------------

    private IEnumerator FadeGroup(CanvasGroup g, float from, float to, float duration)
    {
        float t = 0f;
        g.alpha = from;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            g.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
            yield return null;
        }
        g.alpha = to;
    }

    // Los cuadros negros se encogen y giran, revelando la imagen (en ola diagonal + azar)
    private IEnumerator TileReveal()
    {
        int n = _tileRts.Length;
        float animTime = transitionDuration * 0.4f;
        float spread = transitionDuration - animTime;
        float[] delays = new float[n];

        for (int y = 0; y < tileRows; y++)
        {
            for (int x = 0; x < tileColumns; x++)
            {
                float diag = (x / (float)Mathf.Max(1, tileColumns - 1) + y / (float)Mathf.Max(1, tileRows - 1)) * 0.5f;
                delays[y * tileColumns + x] = Mathf.Lerp(diag, Random.value, 0.5f) * spread;
            }
        }

        float t = 0f;
        while (t < transitionDuration)
        {
            t += Time.unscaledDeltaTime;
            for (int i = 0; i < n; i++)
            {
                if (!_tileRts[i].gameObject.activeSelf) continue;
                float k = Mathf.Clamp01((t - delays[i]) / animTime);
                if (k <= 0f) continue;
                if (k >= 1f) { _tileRts[i].gameObject.SetActive(false); continue; }
                float e = k * k * (3f - 2f * k);
                _tileRts[i].localScale = Vector3.one * (1f - e);
                _tileRts[i].localEulerAngles = new Vector3(0f, 0f, 90f * e);
            }
            yield return null;
        }

        for (int i = 0; i < n; i++) _tileRts[i].gameObject.SetActive(false);
    }

    private IEnumerator TypeText(GameEvent ev, float narrationLength)
    {
        string full = ev.text ?? "";
        bool narrated = narrationLength > 0f;

        float cps = ev.charsPerSecond > 0f ? ev.charsPerSecond : defaultCharsPerSecond;
        // Con narracion, el texto se escribe al ritmo del audio (termina un poco antes que la voz)
        if (narrated && ev.syncTypingToNarration && full.Length > 0)
            cps = full.Length / Mathf.Max(0.5f, narrationLength - 0.3f);

        int blipEvery = Mathf.Max(1, blipEveryNChars);

        // El texto restante se escribe transparente para que no salten las lineas
        _mainText.text = "<color=#00000000>" + full + "</color>";

        float acc = 0f;
        int shown = 0;
        while (shown < full.Length)
        {
            acc += Time.unscaledDeltaTime * cps;
            int target = Mathf.Min(full.Length, Mathf.FloorToInt(acc));
            if (target != shown)
            {
                if (!narrated && typeBlip != null && target / blipEvery != shown / blipEvery)
                    _audio.PlayOneShot(typeBlip, blipVolume);
                shown = target;
                _mainText.text = full.Substring(0, shown) + "<color=#00000000>" + full.Substring(shown) + "</color>";
            }
            yield return null;
        }

        _mainText.text = full;
    }

    private IEnumerator ShowButtons()
    {
        _buttonsGroup.alpha = 1f;
        _buttonsGroup.interactable = true;
        _buttonsGroup.blocksRaycasts = true;

        const float stagger = 0.1f;
        float total = buttonPopTime + stagger;
        float t = 0f;
        while (t < total)
        {
            t += Time.unscaledDeltaTime;
            for (int i = 0; i < _choices.Length; i++)
            {
                float k = Mathf.Clamp01((t - i * stagger) / Mathf.Max(0.01f, buttonPopTime));
                _choices[i].rt.localScale = Vector3.one * EaseOutBack(k);
            }
            yield return null;
        }
        foreach (var c in _choices) c.rt.localScale = Vector3.one;
    }

    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }

    // ---------------------------------------------------------------------
    //  CONTENIDO DEL EVENTO
    // ---------------------------------------------------------------------

    private void Populate(GameEvent ev)
    {
        _eventImage.sprite = ev.image;
        _eventImage.enabled = ev.image != null;

        _mainText.text = "";
        Color tc = ev.textColor;
        if (tc.a <= 0.01f) tc = Color.white;   // evita texto invisible si el color quedo en 0
        _mainText.color = tc;
        _mainText.fontSize = ev.fontSize > 0 ? ev.fontSize : textFontSize;

        SetupChoice(_choices[0], ev.choiceA);
        SetupChoice(_choices[1], ev.choiceB);
    }

    private void SetupChoice(ChoiceUI ui, EventChoice data)
    {
        ui.label.text = data != null ? data.label : "";
        ui.button.interactable = true;
        BuildArrows(ui.arrowRow, data);
    }

    private void BuildArrows(RectTransform row, EventChoice choice)
    {
        for (int i = row.childCount - 1; i >= 0; i--)
        {
            GameObject old = row.GetChild(i).gameObject;
            old.SetActive(false);
            Destroy(old);
        }
        if (choice == null || choice.effects == null) return;

        foreach (var e in choice.effects)
        {
            if (Mathf.Approximately(e.amount, 0f)) continue;

            RectTransform group = NewRect("Effect", row);
            var hl = group.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 3f;
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childControlWidth = hl.childControlHeight = true;
            hl.childForceExpandWidth = hl.childForceExpandHeight = false;

            if (showBarColorChip)
            {
                RectTransform chip = NewRect("Chip", group);
                var ci = chip.gameObject.AddComponent<Image>();
                ci.raycastTarget = false;
                ci.color = (e.randomBar || StatusBarsUI.Instance == null)
                    ? Color.white
                    : StatusBarsUI.Instance.GetAccentColor(e.bar);
                SetSize(chip, chipSize, chipSize);
            }

            bool up = e.amount > 0f;
            float abs = Mathf.Abs(e.amount);
            int count = abs >= threeArrowsFrom ? 3 : (abs >= twoArrowsFrom ? 2 : 1);

            for (int i = 0; i < count; i++)
            {
                RectTransform a = NewRect("Arrow", group);
                var ai = a.gameObject.AddComponent<Image>();
                ai.sprite = _arrowSprite;
                ai.raycastTarget = false;
                ai.color = up ? arrowUpColor : arrowDownColor;
                if (!up) a.localEulerAngles = new Vector3(0f, 0f, 180f);
                SetSize(a, arrowSize * 0.8f, arrowSize);
            }
        }

        var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
        if (rowLayout != null) rowLayout.spacing = effectSpacing;
    }

    private void ResetTiles()
    {
        foreach (var rt in _tileRts)
        {
            rt.gameObject.SetActive(true);
            rt.localScale = Vector3.one;
            rt.localEulerAngles = Vector3.zero;
        }
    }

    // ---------------------------------------------------------------------
    //  CONSTRUCCION DE LA UI
    // ---------------------------------------------------------------------

    private void BuildUI()
    {
        var cgo = new GameObject("Event Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        cgo.transform.SetParent(transform, false);
        _canvas = cgo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 200;   // por encima del canvas de las barras (100)

        var scaler = cgo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = referenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform canvasRt = cgo.GetComponent<RectTransform>();

        // ---------- Raiz del evento (fondo negro) ----------
        RectTransform evRt = NewRect("Event Root", canvasRt);
        Stretch(evRt);
        _eventRoot = evRt.gameObject;
        var black = _eventRoot.AddComponent<Image>();
        black.color = Color.black;           // raycastTarget true: bloquea clicks al juego
        _rootGroup = _eventRoot.AddComponent<CanvasGroup>();

        // Imagen a pantalla completa: sin bordes ni recortes.
        // Se permite deformar ligeramente la proporcion para llenar toda la pantalla.
        RectTransform holder = NewRect("Image Holder", evRt);
        Stretch(holder);
        RectTransform imgRt = NewRect("Event Image", holder);
        Stretch(imgRt);
        _eventImage = imgRt.gameObject.AddComponent<Image>();
        _eventImage.type = Image.Type.Simple;
        _eventImage.raycastTarget = false;
        _eventImage.preserveAspect = false;

        // Cuadros negros de la transicion
        RectTransform tiles = NewRect("Tiles", evRt);
        Stretch(tiles);
        _tileRts = new RectTransform[tileColumns * tileRows];
        for (int y = 0; y < tileRows; y++)
        {
            for (int x = 0; x < tileColumns; x++)
            {
                RectTransform t = NewRect("Tile", tiles);
                t.anchorMin = new Vector2(x / (float)tileColumns, y / (float)tileRows);
                t.anchorMax = new Vector2((x + 1) / (float)tileColumns, (y + 1) / (float)tileRows);
                t.offsetMin = new Vector2(-0.75f, -0.75f);   // pequeno solape: sin lineas entre cuadros
                t.offsetMax = new Vector2(0.75f, 0.75f);
                t.pivot = new Vector2(0.5f, 0.5f);
                var ti = t.gameObject.AddComponent<Image>();
                ti.color = Color.black;
                ti.raycastTarget = false;
                _tileRts[y * tileColumns + x] = t;
            }
        }

        // Panel de texto
        RectTransform panel = NewRect("Text Panel", evRt);
        SetAnchors(panel, textPanelMin, textPanelMax);
        var pi = panel.gameObject.AddComponent<Image>();
        pi.color = textPanelColor;
        pi.raycastTarget = false;
        _textGroup = panel.gameObject.AddComponent<CanvasGroup>();

        RectTransform textRt = NewRect("Text", panel);
        Stretch(textRt);
        textRt.offsetMin = new Vector2(24f, 16f);
        textRt.offsetMax = new Vector2(-24f, -16f);
        _mainText = AddText(textRt, textFontSize, TextAnchor.UpperLeft, Color.white);
        _mainText.supportRichText = true;   // se usa para la escritura progresiva

        // Botones
        RectTransform btnHolder = NewRect("Buttons", evRt);
        SetAnchors(btnHolder, buttonsAreaMin, buttonsAreaMax);
        var hlg = btnHolder.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = buttonSpacing;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = true;
        _buttonsGroup = btnHolder.gameObject.AddComponent<CanvasGroup>();

        for (int i = 0; i < 2; i++) _choices[i] = CreateChoice(btnHolder, i);

        // ---------- Raiz de alerta (evento ignorado) ----------
        RectTransform alRt = NewRect("Alert Root", canvasRt);
        Stretch(alRt);
        _alertRoot = alRt.gameObject;

        RectTransform flashRt = NewRect("Flash", alRt);
        Stretch(flashRt);
        _flash = flashRt.gameObject.AddComponent<Image>();
        _flash.color = Color.clear;
        _flash.raycastTarget = false;

        RectTransform atRt = NewRect("Alert Text", alRt);
        Stretch(atRt);
        _alertText = AddText(atRt, ignoredFontSize, TextAnchor.MiddleCenter, new Color(1f, 0.25f, 0.25f));
        _alertText.fontStyle = FontStyle.Bold;
        var outline = atRt.gameObject.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(4f, -4f);

        _eventRoot.SetActive(false);
        _alertRoot.SetActive(false);
    }

    private ChoiceUI CreateChoice(RectTransform parent, int index)
    {
        var ui = new ChoiceUI();
        ui.rt = NewRect("Choice " + index, parent);

        var img = ui.rt.gameObject.AddComponent<Image>();
        img.color = Color.white;
        ui.button = ui.rt.gameObject.AddComponent<Button>();
        ui.button.targetGraphic = img;
        var cb = ui.button.colors;
        cb.normalColor = buttonColor;
        cb.highlightedColor = buttonHoverColor;
        cb.selectedColor = buttonColor;
        cb.pressedColor = buttonPressedColor;
        cb.disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.6f);
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.1f;
        ui.button.colors = cb;
        int captured = index;
        ui.button.onClick.AddListener(() => OnChoiceClicked(captured));

        RectTransform labelRt = NewRect("Label", ui.rt);
        SetAnchors(labelRt, new Vector2(0.05f, 0.40f), new Vector2(0.95f, 0.96f));
        ui.label = AddText(labelRt, buttonLabelSize, TextAnchor.MiddleCenter, Color.white);
        ui.label.resizeTextForBestFit = true;
        ui.label.resizeTextMinSize = 12;
        ui.label.resizeTextMaxSize = buttonLabelSize;

        ui.arrowRow = NewRect("Arrows", ui.rt);
        SetAnchors(ui.arrowRow, new Vector2(0.05f, 0.04f), new Vector2(0.95f, 0.38f));
        var hl = ui.arrowRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = effectSpacing;
        hl.childAlignment = TextAnchor.MiddleCenter;
        hl.childControlWidth = hl.childControlHeight = true;
        hl.childForceExpandWidth = hl.childForceExpandHeight = false;

        return ui;
    }

    // ---------------------------------------------------------------------
    //  HELPERS
    // ---------------------------------------------------------------------

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static void SetAnchors(RectTransform rt, Vector2 min, Vector2 max)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static void SetSize(RectTransform rt, float w, float h)
    {
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = w;
        le.minHeight = le.preferredHeight = h;
    }

    private Text AddText(RectTransform rt, int size, TextAnchor anchor, Color color)
    {
        var t = rt.gameObject.AddComponent<Text>();
        t.font = _font;
        t.fontSize = size;
        t.alignment = anchor;
        t.color = color;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        return t;
    }

    private Font GetFont()
    {
        if (font != null) return font;

        Font f = null;
        try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        if (f == null)
        {
            try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
        }
        if (f == null) f = Font.CreateDynamicFontFromOSFont("Arial", textFontSize);
        if (f == null) Debug.LogError("EventManager: no se pudo cargar ninguna fuente. Asigna una en el campo 'font'.", this);
        return f;
    }

    // Flecha hacia arriba generada por codigo (se rota 180 para la flecha hacia abajo)
    private static Sprite BuildArrowSprite()
    {
        const int s = 64;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;

        float headStart = s * 0.45f;
        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                float dx = Mathf.Abs(x + 0.5f - s * 0.5f);
                bool on;
                if (y >= headStart)
                {
                    float half = (s * 0.5f) * (1f - (y - headStart) / (s - headStart));
                    on = dx <= half;
                }
                else
                {
                    on = dx <= s * 0.14f && y >= s * 0.04f;
                }
                tex.SetPixel(x, y, on ? Color.white : Color.clear);
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
    }

    private static BarType RandomBarType()
    {
        var all = (BarType[])System.Enum.GetValues(typeof(BarType));
        return all[Random.Range(0, all.Length)];
    }

    private GameObject GetBarsRoot()
    {
        if (barsUIRoot != null) return barsUIRoot;
        return StatusBarsUI.Instance != null ? StatusBarsUI.Instance.gameObject : null;
    }

    private static void EnsureEventSystem()
    {
#if UNITY_2023_1_OR_NEWER
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;
#else
        if (Object.FindObjectOfType<EventSystem>() != null) return;
#endif
        var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var module = go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        module.AssignDefaultActions();   // sin esto los botones no reciben clicks
#else
        go.AddComponent<StandaloneInputModule>();
#endif
    }
}
