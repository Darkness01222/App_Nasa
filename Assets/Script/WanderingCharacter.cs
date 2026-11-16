using UnityEngine;

/// <summary>
/// Personaje que cada cierto tiempo elige un punto aleatorio cercano dentro de un
/// WalkableArea y camina hacia el. Si el punto es invalido (fuera de la sala, dentro
/// de un obstaculo o con una pared en medio), elige otro.
///
/// Reutilizable: sirve para cualquier personaje y cualquier escenario; solo cambia
/// los sprites y asigna otro WalkableArea.
///
/// Requisitos en el GameObject: SpriteRenderer, Rigidbody2D, CircleCollider2D
/// (se agregan solos con RequireComponent).
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
public class WanderingCharacter : MonoBehaviour
{
    private enum State { Idle, WalkLeft, WalkRight }

    [Header("Escenario")]
    [Tooltip("Si se deja vacio, busca uno en la escena automaticamente")]
    public WalkableArea area;

    [Header("Sprites (arrastra aqui los sprites)")]
    [Tooltip("Sprite(s) cuando esta quieto")]
    public Sprite[] idleSprites;
    [Tooltip("Sprite(s) al caminar a la izquierda")]
    public Sprite[] walkLeftSprites;
    [Tooltip("Sprite(s) al caminar a la derecha")]
    public Sprite[] walkRightSprites;
    [Tooltip("Cuadros por segundo si algun estado tiene varios sprites")]
    public float animationFps = 6f;

    [Header("Movimiento")]
    public float moveSpeed = 1f;
    [Tooltip("Radio del personaje en los pies, para no pegarse a las paredes")]
    public float bodyRadius = 0.15f;

    [Header("Seleccion de destino")]
    [Tooltip("Tiempo minimo entre decisiones (segundos)")]
    public float minWaitTime = 1.5f;
    [Tooltip("Tiempo maximo entre decisiones (segundos)")]
    public float maxWaitTime = 4f;
    [Tooltip("Distancia minima del punto elegido")]
    public float minDistance = 0.5f;
    [Tooltip("Distancia maxima del punto elegido")]
    public float maxDistance = 2.5f;
    [Tooltip("Intentos para encontrar un punto valido antes de rendirse")]
    public int maxAttempts = 20;

    [Header("Otros")]
    [Tooltip("Ordena el dibujado segun la posicion Y (efecto de profundidad)")]
    public bool sortByY = true;
    [Tooltip("Segundos sin avanzar para considerar que se atoro y cambiar de destino")]
    public float stuckTime = 0.6f;

    private SpriteRenderer _sr;
    private Rigidbody2D _rb;
    private CircleCollider2D _col;

    private State _state = State.Idle;
    private Vector2 _target;
    private bool _hasTarget;
    private float _waitTimer;
    private float _animTimer;
    private float _stuckTimer;
    private Vector2 _lastPos;

    private void Reset()
    {
        // Valores por defecto al agregar el componente
        var rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        GetComponent<CircleCollider2D>().radius = 0.15f;
    }

    private void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        _rb = GetComponent<Rigidbody2D>();
        _col = GetComponent<CircleCollider2D>();

        _rb.gravityScale = 0f;
        _rb.freezeRotation = true;
        _rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        if (area == null)
        {
#if UNITY_2023_1_OR_NEWER
            area = FindFirstObjectByType<WalkableArea>();
#else
            area = FindObjectOfType<WalkableArea>();
#endif
        }
        if (area == null)
            Debug.LogWarning("WanderingCharacter: no hay WalkableArea en la escena.", this);
    }

    private void Start()
    {
        _waitTimer = Random.Range(minWaitTime, maxWaitTime);
        _lastPos = _rb.position;
        ApplySprite(true);
    }

    private void Update()
    {
        // Mientras esta quieto, cuenta el tiempo para elegir un nuevo punto
        if (!_hasTarget)
        {
            _waitTimer -= Time.deltaTime;
            if (_waitTimer <= 0f)
            {
                _waitTimer = Random.Range(minWaitTime, maxWaitTime);
                TryPickTarget();
            }
        }

        ApplySprite(false);

        if (sortByY)
            _sr.sortingOrder = -Mathf.RoundToInt(transform.position.y * 100f);
    }

    private void FixedUpdate()
    {
        if (!_hasTarget) return;

        Vector2 pos = _rb.position;
        Vector2 toTarget = _target - pos;
        float step = moveSpeed * Time.fixedDeltaTime;

        // Llego al destino
        if (toTarget.magnitude <= step)
        {
            _rb.MovePosition(_target);
            StopWalking();
            return;
        }

        // Se atoro (por ejemplo contra otro collider)
        _stuckTimer += Time.fixedDeltaTime;
        if (_stuckTimer >= stuckTime)
        {
            if (Vector2.Distance(pos, _lastPos) < step * 0.5f * (stuckTime / Time.fixedDeltaTime))
            {
                StopWalking();
                return;
            }
            _stuckTimer = 0f;
            _lastPos = pos;
        }

        Vector2 dir = toTarget.normalized;

        // Estado segun la direccion horizontal
        if (Mathf.Abs(dir.x) > 0.01f)
            _state = dir.x < 0f ? State.WalkLeft : State.WalkRight;

        _rb.MovePosition(pos + dir * step);
    }

    // ---------------------------------------------------------------------

    private void TryPickTarget()
    {
        if (area == null) return;

        Vector2 origin = _rb.position;

        for (int i = 0; i < maxAttempts; i++)
        {
            // Punto aleatorio en un anillo alrededor del personaje
            Vector2 dir = Random.insideUnitCircle.normalized;
            float dist = Random.Range(minDistance, maxDistance);
            Vector2 candidate = origin + dir * dist;

            // Descartar si esta fuera de la sala, dentro de un obstaculo
            // o si hay una pared entre el personaje y el punto
            if (!area.IsWalkable(candidate, bodyRadius)) continue;
            if (!area.IsPathClear(origin, candidate, bodyRadius)) continue;

            _target = candidate;
            _hasTarget = true;
            _stuckTimer = 0f;
            _lastPos = origin;
            _state = dir.x < 0f ? State.WalkLeft : State.WalkRight;
            return;
        }
        // Si no encontro nada, se queda quieto y reintenta en la siguiente espera
    }

    private void StopWalking()
    {
        _hasTarget = false;
        _state = State.Idle;
        _stuckTimer = 0f;
    }

    // ---------------------------------------------------------------------
    //  SPRITES
    // ---------------------------------------------------------------------

    private Sprite[] _currentSet;
    private int _currentFrame;

    private void ApplySprite(bool force)
    {
        Sprite[] set = _state == State.WalkLeft ? walkLeftSprites
                     : _state == State.WalkRight ? walkRightSprites
                     : idleSprites;

        if (set == null || set.Length == 0) return;

        // Cambio de estado: reinicia la animacion
        if (set != _currentSet || force)
        {
            _currentSet = set;
            _currentFrame = 0;
            _animTimer = 0f;
        }
        else if (set.Length > 1)
        {
            _animTimer += Time.deltaTime;
            float frameTime = 1f / Mathf.Max(0.01f, animationFps);
            while (_animTimer >= frameTime)
            {
                _animTimer -= frameTime;
                _currentFrame = (_currentFrame + 1) % set.Length;
            }
        }

        _sr.sprite = set[_currentFrame];
    }

    // Gizmo del destino actual
    private void OnDrawGizmosSelected()
    {
        if (_hasTarget)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, _target);
            Gizmos.DrawWireSphere(_target, 0.08f);
        }
    }
}
