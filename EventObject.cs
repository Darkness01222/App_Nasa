using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Pegalo a un objeto con SpriteRenderer + Collider2D.
/// Cada X segundos empieza a parpadear (y se vuelve clickeable).
///  - Si lo clickeas  -> se lanza un evento del EventManager.
///  - Si pasa 'responseTime' sin clickear -> se considera ignorado (castigo).
///
/// El click se detecta aqui mismo (mouse o touch), SIN usar EventSystem ni
/// Physics2DRaycaster, para no interferir con los clicks del resto del juego.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class EventObject : MonoBehaviour
{
    [Header("Ciclo")]
    [Tooltip("X: segundos de espera entre un parpadeo y el siguiente")]
    public float timeBetweenBlinks = 20f;
    [Tooltip("Variacion aleatoria (+/-) para que no sea predecible")]
    public float randomVariation = 4f;
    [Tooltip("Segundos que tiene el jugador para clickear antes de fallar")]
    public float responseTime = 6f;

    [Header("Parpadeo")]
    public float blinkSpeed = 3f;               // ciclos por segundo
    public Color blinkColor = new Color(1f, 0.9f, 0.3f);
    public float blinkScale = 1.15f;
    [Tooltip("Opcional: objeto hijo (ej. un '!') que solo se ve mientras parpadea")]
    public GameObject indicator;

    [Header("Evento")]
    [Tooltip("-1 = evento al azar de la lista del EventManager. Otro valor = indice fijo")]
    public int fixedEventIndex = -1;

    private enum State { Waiting, Blinking, Busy }
    private State _state;
    private float _timer;
    private float _clock;
    private SpriteRenderer _sr;
    private Collider2D[] _cols;
    private Color _baseColor = Color.white;
    private Vector3 _baseScale;

    private void Awake()
    {
        _sr = GetComponentInChildren<SpriteRenderer>();
        if (_sr != null) _baseColor = _sr.color;
        else Debug.LogWarning("EventObject: no hay SpriteRenderer, solo parpadeara por escala.", this);
        _baseScale = transform.localScale;
        _cols = GetComponentsInChildren<Collider2D>();
        if (indicator != null) indicator.SetActive(false);
    }

    private void Start()
    {
        ScheduleNext();
    }

    private void Update()
    {
        // Time.deltaTime = 0 mientras el juego esta pausado, asi que los timers se congelan solos.
        if (EventManager.Instance == null) return;
        float dt = Time.deltaTime;

        switch (_state)
        {
            case State.Waiting:
                _timer -= dt;
                if (_timer <= 0f) StartBlinking();
                break;

            case State.Blinking:
                _timer -= dt;
                _clock += dt;
                AnimateBlink();

                Vector2 screenPos;
                if (Time.timeScale > 0f && PointerPressed(out screenPos) && IsOverMe(screenPos))
                {
                    OnClicked();
                    break;
                }
                if (_timer <= 0f) OnIgnored();
                break;
        }
    }

    // ---------------------------------------------------------------------

    private void ScheduleNext()
    {
        _state = State.Waiting;
        _timer = Mathf.Max(0.5f, timeBetweenBlinks + Random.Range(-randomVariation, randomVariation));
    }

    /// <summary>Fuerza el parpadeo ahora (util para probar).</summary>
    [ContextMenu("Forzar parpadeo")]
    public void StartBlinking()
    {
        if (_state == State.Busy) return;
        _state = State.Blinking;
        _timer = responseTime;
        _clock = 0f;
        if (indicator != null) indicator.SetActive(true);
    }

    private void AnimateBlink()
    {
        float w = 0.5f + 0.5f * Mathf.Sin(_clock * blinkSpeed * Mathf.PI * 2f);
        if (_sr != null) _sr.color = Color.Lerp(_baseColor, blinkColor, w);
        transform.localScale = _baseScale * Mathf.Lerp(1f, blinkScale, w);
    }

    private void StopBlink()
    {
        if (_sr != null) _sr.color = _baseColor;
        transform.localScale = _baseScale;
        if (indicator != null) indicator.SetActive(false);
    }

    // ---------------------------------------------------------------------
    //  CLICK
    // ---------------------------------------------------------------------

    private static bool PointerPressed(out Vector2 pos)
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var m = Mouse.current;
        if (m != null && m.leftButton.wasPressedThisFrame) { pos = m.position.ReadValue(); return true; }
        var ts = Touchscreen.current;
        if (ts != null && ts.primaryTouch.press.wasPressedThisFrame) { pos = ts.primaryTouch.position.ReadValue(); return true; }
        pos = Vector2.zero;
        return false;
#else
        if (Input.GetMouseButtonDown(0)) { pos = Input.mousePosition; return true; }
        if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began) { pos = Input.GetTouch(0).position; return true; }
        pos = Vector2.zero;
        return false;
#endif
    }

    private bool IsOverMe(Vector2 screenPos)
    {
        Camera cam = Camera.main;
        if (cam == null) return false;
        Vector2 world = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, Mathf.Abs(cam.transform.position.z)));
        foreach (var c in _cols)
            if (c != null && c.enabled && c.OverlapPoint(world)) return true;
        return false;
    }

    private void OnClicked()
    {
        var mgr = EventManager.Instance;
        if (mgr == null || mgr.IsBusy) return;

        GameEvent ev = mgr.PickEvent(fixedEventIndex);
        if (ev == null)
        {
            Debug.LogWarning("EventObject: el EventManager no tiene eventos en la lista.", this);
            return;
        }

        _state = State.Busy;
        StopBlink();
        mgr.StartEvent(ev, OnFlowFinished);
    }

    private void OnIgnored()
    {
        StopBlink();
        var mgr = EventManager.Instance;

        // Si otro evento esta en curso, simplemente reinicia sin castigo
        if (mgr == null || mgr.IsBusy)
        {
            ScheduleNext();
            return;
        }

        _state = State.Busy;
        mgr.HandleIgnored(OnFlowFinished);
    }

    private void OnFlowFinished()
    {
        ScheduleNext();
    }
}
