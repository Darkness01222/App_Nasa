using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

/// <summary>
/// Se agrega al personaje (junto a WanderingCharacter). Cada cierto tiempo muestra un
/// icono de exclamacion sobre su cabeza y, junto a el, un cuadro de dialogo con una frase
/// pidiendo ayuda al Comandante (leida de un archivo .txt). Al hacer click sobre el personaje,
/// el icono o el cuadro de texto:
///   - se ocultan el icono y el cuadro,
///   - se crea un efecto (chispas + "salto" del personaje + latido de la barra),
///   - sube un valor aleatorio de una barra aleatoria (logica y visualmente).
///
/// El cuadro de dialogo sigue al personaje y desaparece cuando desaparece el icono
/// (por click o por tiempo).
///
/// No usa colliders para el click: compara la posicion del mouse/toque con los bounds
/// del sprite del personaje, del icono y del cuadro. Funciona con el Input Manager clasico
/// y con el nuevo Input System, y en WebGL / movil.
///
/// FORMATO DEL ARCHIVO DE DIALOGOS (.txt):
///   - Una frase por linea.
///   - Las lineas vacias se ignoran.
///   - Las lineas que empiezan con # son comentarios y se ignoran.
///   - Para un salto de linea dentro de una frase escribe \n
///   Para agregar mas dialogos solo se agregan mas lineas al archivo.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class CharacterNeedIcon : MonoBehaviour
{
    [System.Serializable]
    public class ReplenishEvent : UnityEvent<BarType, int> { }

    [Header("Icono")]
    public Sprite exclamationSprite;
    [Tooltip("Desplazamiento respecto a la parte superior del sprite del personaje")]
    public Vector2 iconOffset = new Vector2(0f, 0.12f);
    public float iconScale = 1f;
    public float bobHeight = 0.04f;
    public float bobSpeed = 5f;

    [Header("Tiempos")]
    public float minInterval = 8f;
    public float maxInterval = 20f;
    [Tooltip("Segundos que permanece el icono visible antes de desaparecer solo si nadie hace click. Si es 0 o menor se usa el valor por defecto (5s)")]
    public float autoHideAfter = 5f;

    [Header("Recompensa")]
    public int minReplenish = 5;
    public int maxReplenish = 15;

    [Header("Click")]
    [Tooltip("Margen extra alrededor del personaje y del icono para facilitar el click")]
    public float clickPadding = 0.05f;
    public Camera cam;

    [Header("Efecto")]
    [Tooltip("Opcional: prefab propio (particulas, animacion...). Si esta vacio se usa el efecto por codigo")]
    public GameObject effectPrefab;
    public int sparkleCount = 14;

    [Header("Eventos")]
    public ReplenishEvent onReplenished;

    [Header("Dialogo")]
    [Tooltip("Muestra un cuadro de texto mientras el icono esta visible")]
    public bool showDialog = true;
    [Tooltip("Archivo .txt con los dialogos (una frase por linea). Arrastralo aqui")]
    public TextAsset dialoguesFile;
    [Tooltip("Alternativa: ruta dentro de una carpeta Resources, sin extension. Se usa solo si no hay archivo asignado arriba")]
    public string dialoguesResourcePath = "Dialogos/ayuda_comandante";
    [Tooltip("Fuente de TextMeshPro (opcional, si esta vacio usa la fuente por defecto de TMP)")]
    public TMP_FontAsset dialogFont;
    public float dialogFontSize = 3f;
    public Color dialogTextColor = Color.white;
    public Color dialogBackgroundColor = new Color(0f, 0f, 0f, 0.75f);
    [Tooltip("Ancho maximo del cuadro en unidades de mundo (el texto hace salto de linea al llegar a este ancho)")]
    public float dialogMaxWidth = 2f;
    [Tooltip("Espacio interior entre el texto y el borde del cuadro")]
    public Vector2 dialogPadding = new Vector2(0.12f, 0.08f);
    [Tooltip("Desplazamiento del cuadro respecto a la parte superior del icono")]
    public Vector2 dialogOffset = new Vector2(0f, 0.05f);

    [Header("Agradecimiento (al hacer click)")]
    [Tooltip("Texto que se muestra al hacer click sobre el personaje")]
    public string thanksMessage = "Gracias capitán";
    [Tooltip("Segundos que se muestra el mensaje de agradecimiento antes de desaparecer")]
    public float thanksDuration = 1f;

    private const int TopSortingOrder = 30000;
    private const float DefaultAutoHideAfter = 5f;

    // Frases de respaldo si no se encuentra el archivo o esta vacio
    private static readonly string[] FallbackDialogues =
    {
        "Comandante, necesito su ayuda.",
        "Comandante, por favor, ¿tiene un momento?"
    };

    private SpriteRenderer _sr;
    private SpriteRenderer _iconSr;
    private Transform _iconTf;
    private bool _iconVisible;
    private float _timer;
    private float _visibleTime;
    private Coroutine _popRoutine;
    private Vector3 _baseScale;

    // Dialogo
    private Transform _dialogTf;
    private SpriteRenderer _dialogBgSr;
    private TextMeshPro _dialogText;
    private List<string> _dialogues;
    private int _lastDialogueIndex = -1;
    private bool _thanksActive;
    private float _thanksTimer;

    private static Sprite _whiteSprite;

    private void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        _baseScale = transform.localScale;
        if (cam == null) cam = Camera.main;

        // Icono: objeto independiente (no hereda la escala/saltos del personaje)
        var go = new GameObject(name + " - Need Icon");
        _iconTf = go.transform;
        _iconSr = go.AddComponent<SpriteRenderer>();
        _iconSr.sprite = exclamationSprite;
        _iconSr.sortingLayerID = _sr.sortingLayerID;
        _iconSr.sortingOrder = TopSortingOrder;
        go.SetActive(false);

        if (showDialog)
        {
            LoadDialogues();
            BuildDialog();
        }

        ResetTimer();
    }

    private void OnDestroy()
    {
        if (_iconTf != null) Destroy(_iconTf.gameObject);
        if (_dialogTf != null) Destroy(_dialogTf.gameObject);
    }

    private void OnDisable()
    {
        if (_iconTf != null) _iconTf.gameObject.SetActive(false);
        if (_dialogTf != null) _dialogTf.gameObject.SetActive(false);
        _iconVisible = false;
        _thanksActive = false;
    }

    private void Update()
    {
        // Mensaje de agradecimiento: sigue al personaje y se oculta al terminar su tiempo
        if (_thanksActive)
        {
            _thanksTimer -= Time.deltaTime;
            if (_thanksTimer <= 0f)
            {
                _thanksActive = false;
                HideDialog();
            }
            else
            {
                UpdateThanksPosition();
            }
        }

        if (!_iconVisible)
        {
            _timer -= Time.deltaTime;
            if (_timer <= 0f) ShowIcon();
            return;
        }

        UpdateIconPosition();

        // El icono siempre desaparece pasado el tiempo configurado
        float hideAfter = autoHideAfter > 0f ? autoHideAfter : DefaultAutoHideAfter;
        _visibleTime += Time.deltaTime;
        if (_visibleTime >= hideAfter)
        {
            HideIcon();
            ResetTimer();
            return;
        }

        Vector2 screenPos;
        if (TryGetClick(out screenPos) && IsOverCharacterOrIcon(screenPos))
            OnClicked();
    }

    // ---------------------------------------------------------------------
    //  ICONO
    // ---------------------------------------------------------------------

    private void ShowIcon()
    {
        _thanksActive = false;
        _iconVisible = true;
        _visibleTime = 0f;
        _iconTf.localScale = Vector3.one * iconScale;
        _iconTf.gameObject.SetActive(true);

        ShowDialog();
        UpdateIconPosition();
    }

    private void HideIcon()
    {
        _iconVisible = false;
        _iconTf.gameObject.SetActive(false);
        HideDialog();
    }

    private void UpdateIconPosition()
    {
        Bounds b = _sr.bounds;
        float bob = Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        // El pivote del icono es el centro; se sube la mitad de su alto para que quede sobre la cabeza
        float half = _iconSr.sprite != null ? _iconSr.sprite.bounds.extents.y * iconScale : 0f;
        float baseY = b.max.y + iconOffset.y + half;

        _iconTf.position = new Vector3(
            b.center.x + iconOffset.x,
            baseY + bob,
            transform.position.z);

        // El cuadro de dialogo se coloca sobre el icono (sin el balanceo, para que el texto no tiemble)
        if (_dialogTf != null && _dialogTf.gameObject.activeSelf)
        {
            _dialogTf.position = new Vector3(
                b.center.x + iconOffset.x + dialogOffset.x,
                baseY + half + dialogOffset.y,
                transform.position.z);
        }
    }

    private void ResetTimer()
    {
        _timer = Random.Range(minInterval, maxInterval);
    }

    // ---------------------------------------------------------------------
    //  DIALOGO
    // ---------------------------------------------------------------------

    private void LoadDialogues()
    {
        _dialogues = new List<string>();

        TextAsset asset = dialoguesFile;
        if (asset == null && !string.IsNullOrEmpty(dialoguesResourcePath))
            asset = Resources.Load<TextAsset>(dialoguesResourcePath);

        if (asset != null)
        {
            string[] lines = asset.text.Split('\n');
            foreach (string raw in lines)
            {
                string line = raw.Trim();               // quita espacios y \r
                if (line.Length == 0) continue;         // linea vacia
                if (line.StartsWith("#")) continue;     // comentario
                _dialogues.Add(line.Replace("\\n", "\n"));
            }
        }
        else
        {
            Debug.LogWarning("CharacterNeedIcon: no se encontro el archivo de dialogos. " +
                             "Asigna 'dialoguesFile' o crea Resources/" + dialoguesResourcePath + ".txt", this);
        }

        if (_dialogues.Count == 0)
            _dialogues.AddRange(FallbackDialogues);
    }

    private void BuildDialog()
    {
        var root = new GameObject(name + " - Need Dialog");
        _dialogTf = root.transform;

        // Fondo
        var bg = new GameObject("Background");
        bg.transform.SetParent(_dialogTf, false);
        _dialogBgSr = bg.AddComponent<SpriteRenderer>();
        _dialogBgSr.sprite = GetWhiteSprite();
        _dialogBgSr.color = dialogBackgroundColor;
        _dialogBgSr.sortingLayerID = _sr.sortingLayerID;
        _dialogBgSr.sortingOrder = TopSortingOrder;

        // Texto
        var txt = new GameObject("Text");
        txt.transform.SetParent(_dialogTf, false);
        _dialogText = txt.AddComponent<TextMeshPro>();
        if (dialogFont != null) _dialogText.font = dialogFont;
        _dialogText.fontSize = dialogFontSize;
        _dialogText.color = dialogTextColor;
        _dialogText.alignment = TextAlignmentOptions.Center;
#pragma warning disable 618
        _dialogText.enableWordWrapping = true;
#pragma warning restore 618
        _dialogText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        _dialogText.sortingLayerID = _sr.sortingLayerID;
        _dialogText.sortingOrder = TopSortingOrder + 1;

        root.SetActive(false);
    }

    private void ShowDialog()
    {
        if (!showDialog || _dialogTf == null || _dialogues == null || _dialogues.Count == 0) return;

        // Elegir una frase al azar evitando repetir la anterior
        int index = Random.Range(0, _dialogues.Count);
        if (_dialogues.Count > 1 && index == _lastDialogueIndex)
            index = (index + 1 + Random.Range(0, _dialogues.Count - 1)) % _dialogues.Count;
        _lastDialogueIndex = index;

        ApplyDialogText(_dialogues[index]);
    }

    // Asigna el texto, ajusta el tamano del cuadro y lo muestra
    private void ApplyDialogText(string message)
    {
        _dialogText.text = message;

        // Medir el texto y ajustar el fondo. La raiz queda en la base-centro del cuadro,
        // asi el cuadro crece hacia arriba desde el icono.
        Vector2 pref = _dialogText.GetPreferredValues(message, dialogMaxWidth, 0f);
        float textW = Mathf.Min(pref.x, dialogMaxWidth) + 0.02f;
        float textH = pref.y;
        float boxW = textW + dialogPadding.x * 2f;
        float boxH = textH + dialogPadding.y * 2f;

        _dialogText.rectTransform.sizeDelta = new Vector2(textW, textH);
        _dialogText.rectTransform.localPosition = new Vector3(0f, boxH * 0.5f, 0f);

        Vector2 spriteSize = _dialogBgSr.sprite.bounds.size;
        _dialogBgSr.transform.localPosition = new Vector3(0f, boxH * 0.5f, 0f);
        _dialogBgSr.transform.localScale = new Vector3(boxW / spriteSize.x, boxH / spriteSize.y, 1f);

        _dialogTf.gameObject.SetActive(true);
    }

    private void HideDialog()
    {
        if (_dialogTf != null) _dialogTf.gameObject.SetActive(false);
    }

    // Muestra "Gracias capitan" (o el texto configurado) durante thanksDuration segundos
    private void StartThanks()
    {
        if (!showDialog || _dialogTf == null || thanksDuration <= 0f || string.IsNullOrEmpty(thanksMessage))
            return;

        ApplyDialogText(thanksMessage.Replace("\\n", "\n"));
        _thanksActive = true;
        _thanksTimer = thanksDuration;
        UpdateThanksPosition();
    }

    // Sin icono visible, el cuadro se coloca directamente sobre la cabeza del personaje
    private void UpdateThanksPosition()
    {
        if (_dialogTf == null) return;

        Bounds b = _sr.bounds;
        _dialogTf.position = new Vector3(
            b.center.x + iconOffset.x + dialogOffset.x,
            b.max.y + iconOffset.y + dialogOffset.y,
            transform.position.z);
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

    private bool IsOverCharacterOrIcon(Vector2 screenPos)
    {
        // Ignorar clicks sobre elementos de UI
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return false;

        if (cam == null) cam = Camera.main;
        if (cam == null) return false;

        Vector3 world = cam.ScreenToWorldPoint(
            new Vector3(screenPos.x, screenPos.y, Mathf.Abs(cam.transform.position.z)));

        if (ContainsPoint(_sr.bounds, world) || ContainsPoint(_iconSr.bounds, world))
            return true;

        // Tambien se puede hacer click sobre el cuadro de dialogo
        if (_dialogBgSr != null && _dialogTf.gameObject.activeSelf && ContainsPoint(_dialogBgSr.bounds, world))
            return true;

        return false;
    }

    private bool ContainsPoint(Bounds b, Vector3 p)
    {
        b.Expand(clickPadding * 2f);
        p.z = b.center.z;
        return b.Contains(p);
    }

    // ---------------------------------------------------------------------
    //  RECOMPENSA + EFECTO
    // ---------------------------------------------------------------------

    private void OnClicked()
    {
        HideIcon();
        StartThanks();
        ResetTimer();

        int amount = Random.Range(minReplenish, maxReplenish + 1);
        BarType type = BarType.Energia;
        Color color = Color.white;

        if (StatusBarsUI.Instance != null)
        {
            type = StatusBarsUI.Instance.AddToRandomBar(amount);
            color = StatusBarsUI.Instance.GetAccentColor(type);
        }
        else
        {
            Debug.LogWarning("CharacterNeedIcon: no hay StatusBarsUI en la escena.", this);
        }

        PlayEffect(color);
        onReplenished?.Invoke(type, amount);
    }

    private void PlayEffect(Color color)
    {
        Vector3 pos = _sr.bounds.center;

        if (effectPrefab != null)
            Instantiate(effectPrefab, pos, Quaternion.identity);
        else
            StartCoroutine(SparkleRoutine(pos, color));

        if (_popRoutine != null) StopCoroutine(_popRoutine);
        _popRoutine = StartCoroutine(PopRoutine());
    }

    // Pequeno "salto" elastico del personaje
    private IEnumerator PopRoutine()
    {
        const float duration = 0.3f;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Sin(Mathf.Clamp01(t / duration) * Mathf.PI);
            transform.localScale = new Vector3(
                _baseScale.x * (1f + 0.15f * k),
                _baseScale.y * (1f + 0.25f * k),
                _baseScale.z);
            yield return null;
        }
        transform.localScale = _baseScale;
        _popRoutine = null;
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

    // Explosion de chispas del color de la barra repuesta
    private IEnumerator SparkleRoutine(Vector3 center, Color color)
    {
        Sprite white = GetWhiteSprite();

        int n = Mathf.Max(1, sparkleCount);
        var tfs = new Transform[n];
        var srs = new SpriteRenderer[n];
        var vel = new Vector2[n];
        var life = new float[n];

        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("Sparkle");
            go.transform.position = center;
            go.transform.localScale = Vector3.one * Random.Range(0.8f, 1.8f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = white;
            sr.color = color;
            sr.sortingLayerID = _sr.sortingLayerID;
            sr.sortingOrder = TopSortingOrder - 1;

            float angle = Random.value * Mathf.PI * 2f;
            float speed = Random.Range(0.6f, 1.6f);
            vel[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
            life[i] = Random.Range(0.45f, 0.8f);
            tfs[i] = go.transform;
            srs[i] = sr;
        }

        float t = 0f;
        float maxLife = 0.8f;
        while (t < maxLife)
        {
            t += Time.deltaTime;
            for (int i = 0; i < n; i++)
            {
                if (tfs[i] == null) continue;
                vel[i].y -= 2.5f * Time.deltaTime; // gravedad suave
                tfs[i].position += (Vector3)(vel[i] * Time.deltaTime);
                Color c = srs[i].color;
                c.a = Mathf.Clamp01(1f - t / life[i]);
                srs[i].color = c;
            }
            yield return null;
        }

        for (int i = 0; i < n; i++)
            if (tfs[i] != null) Destroy(tfs[i].gameObject);
    }
}
