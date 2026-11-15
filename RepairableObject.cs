using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

/// <summary>
/// Se agrega a cualquier objeto con SpriteRenderer (cada objeto puede tener su propia configuracion).
///
/// Flujo:
///   1. Pasado un tiempo aleatorio, el objeto "falla": empieza a parpadear en rojo y se vuelve clickeable.
///   2. Se sortea cuantos clicks hacen falta para repararlo (entre minClicks y maxClicks).
///   3. Al hacer click aparece una barra de progreso sobre el objeto. Cada click la hace crecer
///      con efectos (golpe del objeto, chispas, latido y destello de la barra).
///   4. Al llegar al maximo, el objeto deja de parpadear y sube una de las 3 barras de
///      StatusBarsUI (Energia, Radiacion o SoporteVital): al azar o la que elijas.
///
/// Penalizacion (nuevo):
///   - Al fallar el objeto arranca un temporizador. Si no haces el primer click antes de
///     'timeToStartRepair', fallas.
///   - Si empiezas a reparar pero no terminas antes de 'timeToFinishRepair' (contado desde el
///     primer click), tambien fallas.
///   - Al fallar se le restan puntos a una barra de StatusBarsUI al azar, el objeto deja de
///     parpadear y se reproducen efectos (sacudida, chispas, X roja, barra que se vacia, sonido).
///   - Mientras se acaba el tiempo, el parpadeo se acelera para avisar al jugador.
///
/// No usa colliders: compara la posicion del mouse/toque con los bounds del sprite.
/// Funciona con el Input Manager clasico y con el nuevo Input System, y en WebGL / movil.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class RepairableObject : MonoBehaviour
{
    [System.Serializable]
    public class RepairedEvent : UnityEvent<BarType, float> { }

    [System.Serializable]
    public class FailedEvent : UnityEvent<BarType, float> { }

    private enum State { Idle, Faulty }

    [Header("Fallas aleatorias")]
    [Tooltip("Si esta activo, el objeto falla solo en intervalos aleatorios. Si no, usa StartFault() desde codigo")]
    public bool autoStart = true;
    public float minInterval = 10f;
    public float maxInterval = 30f;

    [Header("Reparacion")]
    [Tooltip("Cantidad minima de clicks necesarios para reparar")]
    public int minClicks = 5;
    [Tooltip("Cantidad maxima de clicks necesarios para reparar")]
    public int maxClicks = 12;

    [Header("Recompensa")]
    [Tooltip("Si esta activo sube una barra al azar; si no, sube la barra 'Target Bar'")]
    public bool randomBar = true;
    public BarType targetBar = BarType.Energia;
    public float minReward = 10f;
    public float maxReward = 25f;
    [Tooltip("Opcional: si esta vacio se usa StatusBarsUI.Instance (el objeto BarrasUI de la escena)")]
    public StatusBarsUI statusBars;

    [Header("Penalizacion por fallar")]
    [Tooltip("Si esta activo, no reparar a tiempo quita puntos a una barra al azar")]
    public bool enablePenalty = true;
    [Tooltip("Segundos para EMPEZAR a reparar (primer click) desde que el objeto falla")]
    public float timeToStartRepair = 6f;
    [Tooltip("Segundos para TERMINAR la reparacion, contados desde el primer click")]
    public float timeToFinishRepair = 5f;
    public float minPenalty = 8f;
    public float maxPenalty = 20f;
    [Tooltip("Cuanto se acelera el parpadeo cuando se acaba el tiempo (0 = no se acelera)")]
    public float urgencyBlinkBoost = 2f;

    [Header("Parpadeo")]
    public Color blinkColor = new Color(1f, 0.15f, 0.15f);
    [Tooltip("Ciclos por segundo (aumenta un poco a medida que avanza la reparacion)")]
    public float blinkSpeed = 2.5f;
    [Range(0f, 1f)] public float blinkIntensity = 0.85f;

    [Header("Barra de progreso")]
    [Tooltip("Tamano de la barra en unidades de mundo")]
    public Vector2 barSize = new Vector2(0.9f, 0.14f);
    [Tooltip("Desplazamiento respecto a la parte superior del sprite del objeto")]
    public Vector2 barOffset = new Vector2(0f, 0.15f);
    public float barBorder = 0.03f;
    public Color barBorderColor = new Color(0.05f, 0.05f, 0.08f, 1f);
    public Color barBackColor = new Color(0.20f, 0.20f, 0.24f, 1f);
    public Color barLowColor = new Color(0.95f, 0.25f, 0.20f);
    public Color barMidColor = new Color(1.00f, 0.85f, 0.15f);
    public Color barFullColor = new Color(0.30f, 0.95f, 0.35f);

    [Header("Click")]
    [Tooltip("Margen extra alrededor del objeto para facilitar el click")]
    public float clickPadding = 0.05f;
    public Camera cam;

    [Header("Efectos")]
    [Tooltip("Fuerza del golpe (escala + sacudida) en cada click")]
    public float clickPunch = 0.18f;
    public int clickSparkles = 8;
    public int completeSparkles = 26;
    [Tooltip("Opcional: prefab propio para cada click. Si esta vacio se usan las chispas por codigo")]
    public GameObject clickEffectPrefab;
    [Tooltip("Opcional: prefab propio al terminar. Si esta vacio se usan las chispas por codigo")]
    public GameObject completeEffectPrefab;

    [Header("Efectos de falla")]
    public Color failColor = new Color(0.85f, 0.05f, 0.05f);
    [Tooltip("Distancia de la sacudida del objeto al fallar (unidades de mundo)")]
    public float failShake = 0.12f;
    public int failSparkles = 18;
    [Tooltip("Opcional: prefab propio al fallar. Si esta vacio se usan las chispas por codigo")]
    public GameObject failEffectPrefab;
    [Tooltip("Opcional: sonido al fallar")]
    public AudioClip failSound;
    [Range(0f, 1f)] public float failVolume = 1f;

    [Header("Eventos")]
    public UnityEvent onFaultStarted;
    [Tooltip("Se dispara al reparar: (barra repuesta, cantidad)")]
    public RepairedEvent onRepaired;
    [Tooltip("Se dispara al fallar: (barra penalizada, cantidad quitada)")]
    public FailedEvent onFailed;

    private const int TopSortingOrder = 29000;

    private SpriteRenderer _sr;
    private Color _baseColor;
    private Vector3 _baseScale;
    private Quaternion _baseRot;

    private State _state = State.Idle;
    private float _timer;
    private float _blinkPhase;
    private int _clicksRequired;
    private int _clicksDone;
    private float _progress;      // objetivo (0-1)
    private float _displayed;     // valor mostrado, suavizado (0-1)

    // Penalizacion
    private bool _repairStarted;  // ya se hizo el primer click
    private float _failTimer;     // segundos restantes antes de fallar
    private float _failTimeMax;   // duracion total del tramo actual (para la urgencia)
    private float _barFail;       // 0-1, destello/sacudida roja de la barra al fallar
    private Vector3 _failBasePos;
    private Coroutine _failRoutine;

    // Barra
    private Transform _barRoot;
    private Transform _fillTf;
    private SpriteRenderer _fillSr;
    private float _barUnit = 1f;
    private bool _barShow;
    private float _barHold;
    private float _barAppear;
    private float _barPunch;

    private Coroutine _punchRoutine;
    private Coroutine _flashRoutine;

    private static Sprite _whiteSprite;

    // ---------------------------------------------------------------------
    //  CICLO DE VIDA
    // ---------------------------------------------------------------------

    private void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        _baseColor = _sr.color;
        _baseScale = transform.localScale;
        _baseRot = transform.localRotation;
        if (cam == null) cam = Camera.main;

        BuildBar();
        ResetTimer();
    }

    private void OnValidate()
    {
        minClicks = Mathf.Max(1, minClicks);
        maxClicks = Mathf.Max(minClicks, maxClicks);
        maxInterval = Mathf.Max(minInterval, maxInterval);
        maxReward = Mathf.Max(minReward, maxReward);
        timeToStartRepair = Mathf.Max(0.1f, timeToStartRepair);
        timeToFinishRepair = Mathf.Max(0.1f, timeToFinishRepair);
        minPenalty = Mathf.Max(0f, minPenalty);
        maxPenalty = Mathf.Max(minPenalty, maxPenalty);
    }

    private void OnDestroy()
    {
        if (_barRoot != null) Destroy(_barRoot.gameObject);
    }

    private void OnDisable()
    {
        // Dejar todo limpio si el objeto se desactiva
        _state = State.Idle;
        _progress = _displayed = 0f;
        _barShow = false;
        _barHold = 0f;
        _barFail = 0f;
        CancelFailRoutine();
        if (_barRoot != null) _barRoot.gameObject.SetActive(false);
        if (_sr != null) _sr.color = _baseColor;
        transform.localScale = _baseScale;
        transform.localRotation = _baseRot;
        _punchRoutine = null;
        _flashRoutine = null;
    }

    private void Update()
    {
        // La barra se sigue animando aunque ya este reparado (aparicion/desaparicion)
        if (_barRoot != null && _barRoot.gameObject.activeSelf) UpdateBar();

        if (_state == State.Idle)
        {
            if (!autoStart) return;
            _timer -= Time.deltaTime;
            if (_timer <= 0f) StartFault();
            return;
        }

        UpdateBlink();

        Vector2 screenPos;
        Vector3 world;
        if (TryGetClick(out screenPos) && IsOverObject(screenPos, out world))
            OnClicked(world);

        // Temporizador de falla (despues del click, para que el ultimo click a tiempo cuente)
        if (enablePenalty && _state == State.Faulty)
        {
            _failTimer -= Time.deltaTime;
            if (_failTimer <= 0f) Fail();
        }
    }

    // ---------------------------------------------------------------------
    //  API PUBLICA
    // ---------------------------------------------------------------------

    public bool IsFaulty { get { return _state == State.Faulty; } }
    public float Progress { get { return _progress; } }

    /// <summary>Hace que el objeto falle ahora mismo (parpadeo + clickeable).</summary>
    public void StartFault()
    {
        if (_state == State.Faulty) return;

        if (_flashRoutine != null) { StopCoroutine(_flashRoutine); _flashRoutine = null; }
        CancelFailRoutine();

        _state = State.Faulty;
        _repairStarted = false;
        _failTimeMax = timeToStartRepair;
        _failTimer = timeToStartRepair;
        _clicksDone = 0;
        _clicksRequired = Random.Range(minClicks, maxClicks + 1);
        _progress = 0f;
        _displayed = 0f;
        _blinkPhase = 0f;
        _barShow = false;

        onFaultStarted?.Invoke();
    }

    // ---------------------------------------------------------------------
    //  PARPADEO
    // ---------------------------------------------------------------------

    private void UpdateBlink()
    {
        // Se acumula la fase para que cambiar la velocidad no produzca saltos
        float speed = blinkSpeed * (1f + _progress * 0.8f);

        // Urgencia: el parpadeo se acelera a medida que se acaba el tiempo
        if (enablePenalty && _failTimeMax > 0f)
        {
            float urgency = 1f - Mathf.Clamp01(_failTimer / _failTimeMax);
            speed *= 1f + urgency * urgency * urgencyBlinkBoost;
        }
        _blinkPhase += Time.deltaTime * speed * Mathf.PI * 2f;
        float k = 0.5f + 0.5f * Mathf.Sin(_blinkPhase);

        Color c = Color.Lerp(_baseColor, blinkColor, k * blinkIntensity);
        c.a = _baseColor.a;
        _sr.color = c;
    }

    // ---------------------------------------------------------------------
    //  CLICK
    // ---------------------------------------------------------------------

    private bool TryGetClick(out Vector2 screenPos)
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            screenPos = mouse.position.ReadValue();
            return true;
        }
        var touch = UnityEngine.InputSystem.Touchscreen.current;
        if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
        {
            screenPos = touch.primaryTouch.position.ReadValue();
            return true;
        }
#else
        if (Input.GetMouseButtonDown(0))
        {
            screenPos = Input.mousePosition;
            return true;
        }
        if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
        {
            screenPos = Input.GetTouch(0).position;
            return true;
        }
#endif
        screenPos = default(Vector2);
        return false;
    }

    private bool IsOverObject(Vector2 screenPos, out Vector3 world)
    {
        world = default(Vector3);

        // Ignorar clicks sobre elementos de UI
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return false;

        if (cam == null) cam = Camera.main;
        if (cam == null) return false;

        world = cam.ScreenToWorldPoint(
            new Vector3(screenPos.x, screenPos.y, Mathf.Abs(cam.transform.position.z)));

        Bounds b = _sr.bounds;
        b.Expand(clickPadding * 2f);
        world.z = b.center.z;
        return b.Contains(world);
    }

    private void OnClicked(Vector3 worldPos)
    {
        // Primer click: ahora corre el tiempo para TERMINAR la reparacion
        if (!_repairStarted)
        {
            _repairStarted = true;
            _failTimeMax = timeToFinishRepair;
            _failTimer = timeToFinishRepair;
        }

        _clicksDone++;
        _progress = Mathf.Clamp01((float)_clicksDone / _clicksRequired);

        ShowBar();
        _barPunch = 1f;

        PlayClickEffect(worldPos, completed: false);

        if (_clicksDone >= _clicksRequired)
            Complete(worldPos);
    }

    // ---------------------------------------------------------------------
    //  COMPLETAR + RECOMPENSA
    // ---------------------------------------------------------------------

    private void Complete(Vector3 worldPos)
    {
        _state = State.Idle;
        ResetTimer();

        // La barra llega al 100%, se mantiene un instante y desaparece
        _barShow = false;
        _barHold = 0.45f;

        // Deja de parpadear: pequeno destello blanco y vuelve al color original
        if (_flashRoutine != null) StopCoroutine(_flashRoutine);
        _flashRoutine = StartCoroutine(FlashRoutine());

        // Recompensa: sube una de las barras de StatusBarsUI
        float amount = Random.Range(minReward, maxReward);
        BarType type = targetBar;
        Color accent = barFullColor;

        StatusBarsUI ui = GetStatusBars();
        if (ui != null)
        {
            if (randomBar) type = ui.AddToRandomBar(amount);
            else ui.AddToBar(targetBar, amount);
            accent = ui.GetAccentColor(type);
        }
        else
        {
            Debug.LogWarning("RepairableObject: no se encontro StatusBarsUI (objeto BarrasUI) en la escena.", this);
        }

        PlayClickEffect(_sr.bounds.center, completed: true, accentColor: accent);
        onRepaired?.Invoke(type, amount);
    }

    // ---------------------------------------------------------------------
    //  FALLAR + PENALIZACION
    // ---------------------------------------------------------------------

    private void Fail()
    {
        _state = State.Idle;
        ResetTimer();

        // La barra de progreso se vacia en rojo (si no habia empezado, aparece vacia y roja)
        _progress = 0f;
        _clicksDone = 0;
        ShowBar();
        _barShow = false;
        _barHold = 0.7f;
        _barFail = 1f;

        // Penalizacion: se le quita a una barra de StatusBarsUI al azar
        float amount = Random.Range(minPenalty, maxPenalty);
        BarType type = GetRandomBarType();
        Color accent = failColor;

        StatusBarsUI ui = GetStatusBars();
        if (ui != null)
        {
            ApplyPenalty(ui, type, amount);
            accent = ui.GetAccentColor(type);
        }
        else
        {
            Debug.LogWarning("RepairableObject: no se encontro StatusBarsUI (objeto BarrasUI) en la escena.", this);
        }

        PlayFailEffect(accent);
        onFailed?.Invoke(type, amount);
    }

    // Si tu StatusBarsUI tiene un metodo propio para restar (ej. RemoveFromBar), cambialo aqui.
    private void ApplyPenalty(StatusBarsUI ui, BarType type, float amount)
    {
        ui.AddToBar(type, -amount);
    }

    private static BarType GetRandomBarType()
    {
        var values = (BarType[])System.Enum.GetValues(typeof(BarType));
        return values[Random.Range(0, values.Length)];
    }

    private void CancelFailRoutine()
    {
        if (_failRoutine == null) return;
        StopCoroutine(_failRoutine);
        _failRoutine = null;
        transform.localPosition = _failBasePos;
        transform.localRotation = _baseRot;
        transform.localScale = _baseScale;
        if (_sr != null) _sr.color = _baseColor;
    }

    private StatusBarsUI GetStatusBars()
    {
        if (statusBars != null) return statusBars;
        if (StatusBarsUI.Instance != null) return StatusBarsUI.Instance;

        var go = GameObject.Find("BarrasUI");
        if (go != null) statusBars = go.GetComponent<StatusBarsUI>();
        return statusBars;
    }

    private void ResetTimer()
    {
        _timer = Random.Range(minInterval, maxInterval);
    }

    // ---------------------------------------------------------------------
    //  BARRA DE PROGRESO
    // ---------------------------------------------------------------------

    private void BuildBar()
    {
        Sprite white = GetWhiteSprite();
        _barUnit = white.bounds.size.x;

        var root = new GameObject(name + " - Repair Bar");
        _barRoot = root.transform;

        // Borde
        var border = CreateBarPart("Border", barBorderColor, TopSortingOrder);
        border.transform.localScale = new Vector3(
            (barSize.x + barBorder * 2f) / _barUnit,
            (barSize.y + barBorder * 2f) / _barUnit, 1f);

        // Fondo
        var back = CreateBarPart("Back", barBackColor, TopSortingOrder + 1);
        back.transform.localScale = new Vector3(barSize.x / _barUnit, barSize.y / _barUnit, 1f);

        // Relleno (se escala en X desde la izquierda)
        _fillSr = CreateBarPart("Fill", barLowColor, TopSortingOrder + 2);
        _fillTf = _fillSr.transform;

        root.SetActive(false);
    }

    private SpriteRenderer CreateBarPart(string partName, Color color, int order)
    {
        var go = new GameObject(partName);
        go.transform.SetParent(_barRoot, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetWhiteSprite();
        sr.color = color;
        sr.sortingLayerID = _sr.sortingLayerID;
        sr.sortingOrder = order;
        return sr;
    }

    private void ShowBar()
    {
        if (!_barRoot.gameObject.activeSelf)
        {
            _barRoot.gameObject.SetActive(true);
            _barAppear = 0f;
            _displayed = 0f;
            UpdateBar(); // evita un frame con la posicion anterior
        }
        _barShow = true;
        _barHold = 0f;
    }

    private void UpdateBar()
    {
        float dt = Time.deltaTime;

        // Aparicion / desaparicion con rebote
        float target = (_barShow || _barHold > 0f) ? 1f : 0f;
        if (_barHold > 0f) _barHold -= dt;
        _barAppear = Mathf.MoveTowards(_barAppear, target, dt * 6f);
        if (_barAppear <= 0f && target <= 0f)
        {
            _barRoot.gameObject.SetActive(false);
            return;
        }

        // Progreso suave + latido
        _displayed = Mathf.Lerp(_displayed, _progress, 1f - Mathf.Exp(-14f * dt));
        _barPunch = Mathf.Max(0f, _barPunch - dt * 5f);
        _barFail = Mathf.Max(0f, _barFail - dt * 1.4f);

        // Sigue al objeto
        Bounds b = _sr.bounds;
        float shakeX = Mathf.Sin(Time.time * 70f) * 0.05f * _barFail;
        _barRoot.position = new Vector3(
            b.center.x + barOffset.x + shakeX,
            b.max.y + barOffset.y + barSize.y * 0.5f,
            transform.position.z);

        float s = EaseOutBack(_barAppear);
        _barRoot.localScale = new Vector3(
            s * (1f + 0.10f * _barPunch),
            s * (1f + 0.70f * _barPunch),
            1f);

        // Relleno anclado a la izquierda
        float fw = barSize.x * _displayed;
        _fillTf.localScale = new Vector3(fw / _barUnit, barSize.y / _barUnit, 1f);
        _fillTf.localPosition = new Vector3(-barSize.x * 0.5f + fw * 0.5f, 0f, 0f);

        // Color rojo -> amarillo -> verde, con destello blanco en cada click
        Color c = GetBarColor(_displayed);
        c = Color.Lerp(c, failColor, _barFail);
        _fillSr.color = Color.Lerp(c, Color.white, _barPunch * 0.8f);
    }

    private Color GetBarColor(float p)
    {
        if (p < 0.5f) return Color.Lerp(barLowColor, barMidColor, p / 0.5f);
        return Color.Lerp(barMidColor, barFullColor, (p - 0.5f) / 0.5f);
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }

    // ---------------------------------------------------------------------
    //  EFECTOS
    // ---------------------------------------------------------------------

    private void PlayClickEffect(Vector3 pos, bool completed, Color accentColor = default(Color))
    {
        pos.z = transform.position.z;

        GameObject prefab = completed ? completeEffectPrefab : clickEffectPrefab;
        if (prefab != null)
        {
            Instantiate(prefab, pos, Quaternion.identity);
        }
        else if (completed)
        {
            // Explosion grande del color de la barra repuesta + destellos blancos
            StartCoroutine(BurstRoutine(pos, accentColor, Color.white, completeSparkles,
                1.2f, 3.0f, 1.2f, 2.6f, 0.5f, 0.9f, 2.0f));
        }
        else
        {
            // Chispas rojas/amarillas hacia afuera desde el punto del click
            StartCoroutine(BurstRoutine(pos, blinkColor, barMidColor, clickSparkles,
                0.8f, 2.0f, 0.9f, 1.8f, 0.3f, 0.6f, 3.0f));
        }

        if (_punchRoutine != null) StopCoroutine(_punchRoutine);
        _punchRoutine = StartCoroutine(PunchRoutine(completed ? clickPunch * 2f : clickPunch));
    }

    // Golpe elastico: aplasta/estira el objeto y lo sacude un poco
    private IEnumerator PunchRoutine(float strength)
    {
        const float duration = 0.35f;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            float damp = 1f - k;

            float wave = Mathf.Sin(k * Mathf.PI * 3f) * damp * strength;
            transform.localScale = new Vector3(
                _baseScale.x * (1f + wave),
                _baseScale.y * (1f - wave * 0.8f),
                _baseScale.z);

            float shake = Mathf.Sin(k * Mathf.PI * 6f) * damp * strength * 30f;
            transform.localRotation = _baseRot * Quaternion.Euler(0f, 0f, shake);

            yield return null;
        }
        transform.localScale = _baseScale;
        transform.localRotation = _baseRot;
        _punchRoutine = null;
    }

    // Destello blanco al terminar y regreso al color original
    private IEnumerator FlashRoutine()
    {
        const float duration = 0.3f;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            Color c = Color.Lerp(Color.white, _baseColor, Mathf.Clamp01(t / duration));
            c.a = _baseColor.a;
            _sr.color = c;
            yield return null;
        }
        _sr.color = _baseColor;
        _flashRoutine = null;
    }

    // ---------------------------------------------------------------------
    //  EFECTOS DE FALLA
    // ---------------------------------------------------------------------

    private void PlayFailEffect(Color accent)
    {
        Vector3 center = _sr.bounds.center;
        center.z = transform.position.z;

        if (failEffectPrefab != null)
        {
            Instantiate(failEffectPrefab, center, Quaternion.identity);
        }
        else
        {
            // Chispas oscuras (rojo + color de la barra penalizada) que caen con fuerza
            StartCoroutine(BurstRoutine(center, failColor, accent, failSparkles,
                1.0f, 2.6f, 0.9f, 2.0f, 0.5f, 0.9f, 4.0f));
        }

        if (failSound != null) AudioSource.PlayClipAtPoint(failSound, center, failVolume);

        if (_punchRoutine != null) { StopCoroutine(_punchRoutine); _punchRoutine = null; }
        if (_flashRoutine != null) { StopCoroutine(_flashRoutine); _flashRoutine = null; }
        CancelFailRoutine();
        _failRoutine = StartCoroutine(FailRoutine());

        StartCoroutine(FailMarkRoutine());
    }

    // Sacudida seca del objeto + destello rojo que se desvanece al color original
    private IEnumerator FailRoutine()
    {
        const float duration = 0.55f;
        _failBasePos = transform.localPosition;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            float damp = 1f - k;
            float wave = Mathf.Sin(k * Mathf.PI * 14f) * damp;

            transform.localPosition = _failBasePos + new Vector3(wave * failShake, 0f, 0f);
            transform.localRotation = _baseRot * Quaternion.Euler(0f, 0f, wave * 6f);
            transform.localScale = _baseScale * (1f - 0.06f * damp);

            Color c = Color.Lerp(failColor, _baseColor, Mathf.SmoothStep(0f, 1f, k));
            c.a = _baseColor.a;
            _sr.color = c;

            yield return null;
        }

        transform.localPosition = _failBasePos;
        transform.localRotation = _baseRot;
        transform.localScale = _baseScale;
        _sr.color = _baseColor;
        _failRoutine = null;
    }

    // Una "X" roja con contorno que aparece con rebote sobre el objeto, sube y se desvanece
    private IEnumerator FailMarkRoutine()
    {
        Sprite white = GetWhiteSprite();
        float unit = white.bounds.size.x;

        Bounds b = _sr.bounds;
        Vector3 start = new Vector3(b.center.x, b.max.y + 0.45f, transform.position.z);

        var root = new GameObject(name + " - Fail Mark");
        root.transform.position = start;
        Destroy(root, 1.8f); // seguro por si este componente se desactiva

        var srs = new SpriteRenderer[4];
        for (int i = 0; i < 4; i++)
        {
            bool outline = i < 2;
            var go = new GameObject(outline ? "Outline" : "Stroke");
            go.transform.SetParent(root.transform, false);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, (i % 2 == 0) ? 45f : -45f);

            float len = outline ? 0.64f : 0.52f;
            float thick = outline ? 0.20f : 0.10f;
            go.transform.localScale = new Vector3(len / unit, thick / unit, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = white;
            sr.color = outline ? new Color(0.05f, 0.02f, 0.02f, 1f) : failColor;
            sr.sortingLayerID = _sr.sortingLayerID;
            sr.sortingOrder = TopSortingOrder + (outline ? 4 : 5);
            srs[i] = sr;
        }

        const float popTime = 0.25f;
        const float total = 1.3f;
        float t = 0f;
        while (t < total)
        {
            t += Time.deltaTime;

            root.transform.localScale = Vector3.one * EaseOutBack(Mathf.Clamp01(t / popTime));
            root.transform.position = start + Vector3.up * (0.5f * Mathf.Clamp01(t / total));

            float fade = Mathf.Clamp01(1f - (t - total * 0.55f) / (total * 0.45f));
            for (int i = 0; i < 4; i++)
            {
                Color c = srs[i].color;
                c.a = fade;
                srs[i].color = c;
            }
            yield return null;
        }

        Destroy(root);
    }

    private static Sprite GetWhiteSprite()
    {
        if (_whiteSprite == null)
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            var px = new Color[16];
            for (int i = 0; i < px.Length; i++) px[i] = Color.white;
            tex.SetPixels(px);
            tex.Apply();
            _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 64f);
        }
        return _whiteSprite;
    }

    // Explosion de chispas que giran, se encogen y se desvanecen
    private IEnumerator BurstRoutine(Vector3 center, Color colorA, Color colorB, int count,
        float speedMin, float speedMax, float scaleMin, float scaleMax,
        float lifeMin, float lifeMax, float gravity)
    {
        Sprite white = GetWhiteSprite();

        int n = Mathf.Max(1, count);
        var tfs = new Transform[n];
        var srs = new SpriteRenderer[n];
        var vel = new Vector2[n];
        var life = new float[n];
        var size = new float[n];
        var spin = new float[n];

        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("Sparkle");
            go.transform.position = center;
            size[i] = Random.Range(scaleMin, scaleMax);
            go.transform.localScale = Vector3.one * size[i];

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = white;
            sr.color = Color.Lerp(colorA, colorB, Random.value);
            sr.sortingLayerID = _sr.sortingLayerID;
            sr.sortingOrder = TopSortingOrder + 3;

            float angle = Random.value * Mathf.PI * 2f;
            float speed = Random.Range(speedMin, speedMax);
            vel[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
            life[i] = Random.Range(lifeMin, lifeMax);
            spin[i] = Random.Range(-540f, 540f);
            tfs[i] = go.transform;
            srs[i] = sr;

            // Seguro: se destruye aunque este componente se desactive a mitad del efecto
            Destroy(go, lifeMax + 0.2f);
        }

        float t = 0f;
        while (t < lifeMax)
        {
            t += Time.deltaTime;
            for (int i = 0; i < n; i++)
            {
                if (tfs[i] == null) continue;
                float k = Mathf.Clamp01(t / life[i]);

                vel[i].y -= gravity * Time.deltaTime;
                tfs[i].position += (Vector3)(vel[i] * Time.deltaTime);
                tfs[i].Rotate(0f, 0f, spin[i] * Time.deltaTime);
                tfs[i].localScale = Vector3.one * size[i] * (1f - 0.6f * k);

                Color c = srs[i].color;
                c.a = 1f - k;
                srs[i].color = c;
            }
            yield return null;
        }

        for (int i = 0; i < n; i++)
            if (tfs[i] != null) Destroy(tfs[i].gameObject);
    }
}
