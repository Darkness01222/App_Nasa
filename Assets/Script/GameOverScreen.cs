using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Monitorea las barras de StatusBarsUI. Cuando llegan a cero:
///   1. Pausa el juego (Time.timeScale = 0 y audio en pausa).
///   2. Oscurece la pantalla y muestra una imagen (sprite) de Game Over.
///   3. Pasado 'restartDelay' aparece el texto "CLICK TO RESTART" y, al hacer click, reinicia la escena.
///
/// USO: agrega este componente a un GameObject vacio de la escena de juego y arrastra el sprite.
/// Crea por codigo su propio Canvas y EventSystem (si no existe uno).
/// La escena debe estar en File > Build Settings para poder reiniciarse.
/// </summary>
public class GameOverScreen : MonoBehaviour
{
    public enum Condition { AnyBarReachesZero, AllBarsReachZero }

    [Header("Condicion de Game Over")]
    [Tooltip("AnyBarReachesZero: pierdes cuando UNA barra llega a 0. AllBarsReachZero: cuando TODAS llegan a 0")]
    public Condition condition = Condition.AnyBarReachesZero;

    [Header("Pantalla de Game Over")]
    public Sprite gameOverSprite;
    [Tooltip("Estira la imagen a pantalla completa. Si no, se muestra centrada con su tamano nativo")]
    public bool stretchToScreen = false;
    [Tooltip("Multiplicador del tamano nativo (el Canvas es de referencia 1920x1080)")]
    public float spriteScale = 1f;
    [Range(0f, 1f)] public float dimAmount = 0.75f;
    [Tooltip("Opcional: sonido al perder")]
    public AudioClip gameOverSound;
    [Range(0f, 1f)] public float soundVolume = 1f;

    [Header("Reinicio")]
    [Tooltip("Segundos hasta poder reiniciar. Evita que el click de juego reinicie sin querer")]
    public float restartDelay = 1.5f;
    public string restartText = "CLICK TO RESTART";
    public int restartTextSize = 56;
    public Color restartTextColor = Color.white;
    [Tooltip("Opcional. Si esta vacio se usa la fuente integrada de Unity")]
    public Font customFont;

    [Header("Otros")]
    [Tooltip("Desactiva todos los RepairableObject al perder (siguen leyendo clicks aunque el tiempo este en pausa)")]
    public bool disableRepairables = true;
    public UnityEvent onGameOver;

    private static readonly BarType[] BarTypes = (BarType[])System.Enum.GetValues(typeof(BarType));

    private bool _gameOver;
    private bool _ready;
    private bool _restarting;
    private Image _dim;
    private Image _spriteImg;
    private Text _text;
    private Button _button;
    private Font _font;

    // ---------------------------------------------------------------------
    //  MONITOREO
    // ---------------------------------------------------------------------

    private void Update()
    {
        if (_gameOver) return;
        if (ConditionMet()) TriggerGameOver();
    }

    private bool ConditionMet()
    {
        StatusBarsUI ui = StatusBarsUI.Instance;
        if (ui == null) return false;

        int found = 0;
        bool any = false, all = true;
        foreach (BarType type in BarTypes)
        {
            StatusBar bar = ui.GetBar(type);
            if (bar == null) continue;

            found++;
            bool zero = bar.GetValue() <= 0f;
            any |= zero;
            all &= zero;
        }

        if (found == 0) return false;
        return condition == Condition.AnyBarReachesZero ? any : all;
    }

    // ---------------------------------------------------------------------
    //  GAME OVER
    // ---------------------------------------------------------------------

    private void TriggerGameOver()
    {
        _gameOver = true;

        if (disableRepairables)
        {
#if UNITY_2023_1_OR_NEWER
            RepairableObject[] objs = FindObjectsByType<RepairableObject>(FindObjectsSortMode.None);
#else
            RepairableObject[] objs = FindObjectsOfType<RepairableObject>();
#endif
            foreach (RepairableObject o in objs) o.enabled = false;
        }

        Time.timeScale = 0f;
        AudioListener.pause = true;

        EnsureEventSystem();
        BuildUI();

        if (gameOverSound != null)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.ignoreListenerPause = true; // para que suene aunque el audio este en pausa
            src.PlayOneShot(gameOverSound, soundVolume);
        }

        onGameOver?.Invoke();
        StartCoroutine(Sequence());
    }

    private IEnumerator Sequence()
    {
        // Todo con tiempo "unscaled" porque el juego esta en pausa
        float t = 0f;
        float intro = Mathf.Max(0.6f, restartDelay);
        while (t < intro)
        {
            t += Time.unscaledDeltaTime;

            _dim.color = new Color(0f, 0f, 0f, dimAmount * Mathf.Clamp01(t / 0.5f));
            if (_spriteImg != null)
                _spriteImg.rectTransform.localScale = Vector3.one * EaseOutBack(Mathf.Clamp01(t / 0.6f));
            yield return null;
        }

        // Ya se puede reiniciar
        _ready = true;
        _button.interactable = true;
        _text.gameObject.SetActive(true);

        while (true)
        {
            float a = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 4f);
            Color c = restartTextColor;
            _text.color = new Color(c.r, c.g, c.b, a);
            yield return null;
        }
    }

    private void Restart()
    {
        if (!_ready || _restarting) return;
        _restarting = true;

        // Restaurar antes de cargar: timeScale y pausa de audio persisten entre escenas
        Time.timeScale = 1f;
        AudioListener.pause = false;

        Scene scene = SceneManager.GetActiveScene();
        if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
        else SceneManager.LoadScene(scene.name);
    }

    private void OnDestroy()
    {
        // Si este objeto se destruye por otra via, no dejar el juego congelado
        if (_gameOver)
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }
    }

    // ---------------------------------------------------------------------
    //  UI
    // ---------------------------------------------------------------------

    private void BuildUI()
    {
        var canvasGo = new GameObject("GameOver Canvas", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200; // por encima de las barras y otros menus
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        var canvasRect = (RectTransform)canvasGo.transform;

        // Fondo oscuro a pantalla completa: tambien es el boton de reinicio y bloquea clicks al juego
        _dim = NewImage("Dim", canvasRect, new Color(0f, 0f, 0f, 0f), true);
        Stretch(_dim.rectTransform);
        _button = _dim.gameObject.AddComponent<Button>();
        _button.transition = Selectable.Transition.None;
        _button.navigation = new Navigation { mode = Navigation.Mode.None };
        _button.interactable = false;
        _button.onClick.AddListener(Restart);

        // Imagen de Game Over
        if (gameOverSprite != null)
        {
            _spriteImg = NewImage("GameOverImage", canvasRect, Color.white, false);
            _spriteImg.sprite = gameOverSprite;
            _spriteImg.preserveAspect = true;
            RectTransform rt = _spriteImg.rectTransform;

            if (stretchToScreen)
            {
                Stretch(rt);
            }
            else
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = gameOverSprite.rect.size * spriteScale;
                rt.anchoredPosition = new Vector2(0f, 60f);
            }
            rt.localScale = Vector3.zero;
        }
        else
        {
            Debug.LogWarning("GameOverScreen: falta asignar 'Game Over Sprite'.", this);
        }

        // Texto "CLICK TO RESTART"
        RectTransform trt = NewRect("RestartText", canvasRect);
        trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0f);
        trt.pivot = new Vector2(0.5f, 0f);
        trt.sizeDelta = new Vector2(1400f, 110f);
        trt.anchoredPosition = new Vector2(0f, 110f);

        _text = trt.gameObject.AddComponent<Text>();
        _text.font = GetFont();
        _text.text = restartText;
        _text.fontSize = restartTextSize;
        _text.fontStyle = FontStyle.Bold;
        _text.color = restartTextColor;
        _text.alignment = TextAnchor.MiddleCenter;
        _text.horizontalOverflow = HorizontalWrapMode.Overflow;
        _text.verticalOverflow = VerticalWrapMode.Overflow;
        _text.raycastTarget = false;

        var outline = trt.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(3f, -3f);

        _text.gameObject.SetActive(false);
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
        go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        go.AddComponent<StandaloneInputModule>();
#endif
    }

    private Font GetFont()
    {
        if (customFont != null) return customFont;
        if (_font == null)
        {
#if UNITY_2022_2_OR_NEWER
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
            _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
        }
        return _font;
    }

    private static RectTransform NewRect(string objName, Transform parent)
    {
        var go = new GameObject(objName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image NewImage(string objName, Transform parent, Color color, bool raycast)
    {
        RectTransform rt = NewRect(objName, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = raycast;
        return img;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }
}
