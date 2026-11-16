using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Menu de introduccion pensado para WebGL. Se agrega a UN objeto vacio de una escena de menu.
/// Construye toda la UI por codigo (Canvas, EventSystem, botones), no hace falta preparar nada mas.
///
/// Flujo:
///   0. (Opcional) pantalla "CLICK PARA COMENZAR": los navegadores bloquean el audio hasta que el
///      usuario hace click, asi que sin esto la voz del primer slide no se escucharia en WebGL.
///   1. Se muestra un slide y suena su voz. Cuando la voz termina aparece el boton "NEXT".
///   2. Al hacer click: transicion llamativa (Persianas / Zoom giratorio / Empujon) y pasa al siguiente slide.
///   3. Tras el ultimo slide: fade-out pixeleado (la pantalla se pixela mas y mas mientras se oscurece).
///   4. Aparece el menu final con dos botones; cada uno carga una escena distinta
///      (tambien con fade-out pixeleado). Las escenas deben estar en File > Build Settings.
///
/// Configuracion: llena la lista 'Slides' (imagen + voz + subtitulo opcional) en el Inspector.
///
/// Botones: pueden ser imagenes (con el texto ya incluido). Asigna los sprites en la seccion
/// "Botones con imagen". Si un sprite queda vacio, ese boton se genera por codigo con color y texto.
/// </summary>
public class IntroMenu : MonoBehaviour
{
    [System.Serializable]
    public class Slide
    {
        public Sprite image;
        [Tooltip("Voz que explica el slide. El boton NEXT aparece cuando termina")]
        public AudioClip voice;
        [Tooltip("Opcional: subtitulo en la parte inferior (util si el navegador bloquea el audio)")]
        [TextArea(1, 3)] public string caption;
    }

    public enum TransitionStyle { Random, Blinds, ZoomSpin, Push }

    // ---------------------------------------------------------------------
    //  CONFIGURACION
    // ---------------------------------------------------------------------

    [Header("Slides")]
    public Slide[] slides;
    [Tooltip("Mantiene la proporcion de la imagen (con barras negras). Si no, se estira a pantalla completa")]
    public bool preserveAspect = false;
    [Tooltip("Muestra 'CLICK PARA COMENZAR' primero. Recomendado en WebGL: desbloquea el audio del navegador")]
    public bool requireStartClick = true;
    public string startLabel = "CLICK PARA COMENZAR";
    [Tooltip("Segundos antes de mostrar NEXT cuando el slide no tiene voz")]
    public float noVoiceDelay = 1.5f;
    [Tooltip("Pausa extra despues de que termina la voz, antes de mostrar NEXT")]
    public float delayAfterVoice = 0.25f;
    [Range(0f, 1f)] public float voiceVolume = 1f;

    [Header("Botones con imagen (texto incluido en la imagen)")]
    [Tooltip("Opcional: imagen de la pantalla de inicio (si esta vacia se usa el texto 'startLabel')")]
    public Sprite startSprite;
    [Tooltip("Imagen del boton NEXT. Si esta vacia se genera un boton con texto")]
    public Sprite nextButtonSprite;
    [Tooltip("Imagen del primer boton del menu final")]
    public Sprite buttonASprite;
    [Tooltip("Imagen del segundo boton del menu final")]
    public Sprite buttonBSprite;
    [Tooltip("Usa el tamano en pixeles del sprite (el Canvas es de referencia 1920x1080). " +
             "Si no, la imagen se ajusta dentro de 'Button Size' conservando su proporcion")]
    public bool useSpriteNativeSize = true;
    [Tooltip("Multiplicador del tamano nativo de las imagenes de boton")]
    public float spriteButtonScale = 1f;
    [Tooltip("Separacion vertical entre los dos botones del menu final")]
    public float menuButtonSpacing = 24f;

    [Header("Boton NEXT")]
    public string nextLabel = "NEXT";
    [Tooltip("Tamano del boton generado por codigo; con imagen y sin tamano nativo, es la caja maxima")]
    public Vector2 nextButtonSize = new Vector2(380f, 110f);
    [Tooltip("Distancia desde la esquina inferior derecha (x negativo = hacia la izquierda)")]
    public Vector2 nextButtonOffset = new Vector2(-80f, 70f);

    [Header("Transiciones entre slides")]
    public TransitionStyle transitionStyle = TransitionStyle.Random;
    public Color accentA = new Color(0.10f, 0.85f, 1.00f);
    public Color accentB = new Color(1.00f, 0.15f, 0.60f);
    [Range(4, 30)] public int blindCount = 14;

    [Header("Fade-out pixeleado")]
    public float pixelFadeDuration = 1.4f;
    [Tooltip("Tamano del pixel (referido a 1080p) en cada paso. Mas valores = mas pasos")]
    public int[] pixelSizes = { 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64, 96 };

    [Header("Menu final")]
    public string menuTitle = "MENU PRINCIPAL";
    [Tooltip("Fondo del menu. Si no se asigna, se genera un fondo espacial degradado automaticamente")]
    public Sprite menuBackground;
    [Tooltip("Tamano maximo de los botones A y B, incluso si sus sprites son enormes")]
    public Vector2 menuButtonSize = new Vector2(420f, 100f);
    [Tooltip("Muestra un panel semitransparente detras de las dos opciones")]
    public bool showMenuPanel = true;
    public Color menuPanelColor = new Color(0.025f, 0.045f, 0.10f, 0.75f);
    public Color menuBackgroundColor = new Color(0.04f, 0.05f, 0.11f, 1f);
    public string buttonALabel = "JUGAR";
    [Tooltip("Nombre exacto de la escena (debe estar en Build Settings)")]
    public string sceneA = "Moon";
    public string buttonBLabel = "OPCIONES";
    public string sceneB = "Mars";

    [Header("Estilo")]
    [Tooltip("Opcional. Si esta vacio se usa la fuente integrada de Unity")]
    public Font customFont;
    public Color buttonColor = new Color(0.10f, 0.12f, 0.25f, 1f);
    public Color buttonTextColor = Color.white;

    // ---------------------------------------------------------------------
    //  ESTADO INTERNO
    // ---------------------------------------------------------------------

    private RectTransform _canvasRect;
    private RectTransform _slideRoot;
    private Image _imgCur, _imgNext;
    private RectTransform _fxRoot;
    private Image[] _strips;
    private Image _flash;
    private RectTransform _captionPanel;
    private Text _captionText;
    private Button _nextButton;
    private ButtonFX _nextFx;
    private Image _startScreen;
    private Text _startText;
    private Image _startImage;
    private CanvasGroup _menuGroup;
    private Text _menuTitleText;
    private ButtonFX[] _menuFx;
    private RawImage _pixelOverlay;
    private AudioSource _audio;
    private Font _font;

    private bool _startClicked;
    private bool _nextClicked;
    private bool _loading;
    private int _lastStyle = -1;
    private Coroutine _shakeRoutine;

    // ---------------------------------------------------------------------
    //  CICLO DE VIDA
    // ---------------------------------------------------------------------

    private void Start()
    {
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.loop = false;
        _audio.volume = voiceVolume;

        EnsureEventSystem();
        BuildUI();
        StartCoroutine(Flow());
    }

    private void OnValidate()
    {
        noVoiceDelay = Mathf.Max(0f, noVoiceDelay);
        delayAfterVoice = Mathf.Max(0f, delayAfterVoice);
        pixelFadeDuration = Mathf.Max(0.2f, pixelFadeDuration);
    }

    // ---------------------------------------------------------------------
    //  FLUJO PRINCIPAL
    // ---------------------------------------------------------------------

    private IEnumerator Flow()
    {
        if (slides == null || slides.Length == 0)
        {
            Debug.LogWarning("IntroMenu: la lista 'Slides' esta vacia, se muestra el menu final directamente.", this);
            ShowMenu();
            yield break;
        }

        // 0. Pantalla de inicio (desbloquea el audio en WebGL)
        if (requireStartClick)
        {
            _startScreen.gameObject.SetActive(true);
            _startClicked = false;
            while (!_startClicked)
            {
                float a = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 3f);
                Color pulse = new Color(1f, 1f, 1f, a);
                if (_startImage != null) _startImage.color = pulse;
                else _startText.color = pulse;
                yield return null;
            }
            _startScreen.gameObject.SetActive(false);
        }

        // Primer slide con fade-in desde negro
        SetSprite(_imgCur, 0);
        yield return StartCoroutine(FadeFlash(Color.black, 1f, 0f, 0.7f));

        for (int i = 0; i < slides.Length; i++)
        {
            Slide s = slides[i];
            ShowCaption(s.caption);

            // 1. Voz del slide
            float wait = noVoiceDelay;
            if (s.voice != null)
            {
                _audio.Stop();
                _audio.clip = s.voice;
                _audio.volume = voiceVolume;
                _audio.Play();
                wait = s.voice.length + delayAfterVoice;
            }
            yield return new WaitForSecondsRealtime(wait);

            // 2. NEXT aparece al terminar la voz
            _nextClicked = false;
            yield return StartCoroutine(PopNext(true));
            while (!_nextClicked) yield return null;

            _audio.Stop();
            yield return StartCoroutine(PopNext(false));
            ShowCaption(null);

            // 3. Transicion al siguiente slide
            if (i < slides.Length - 1)
                yield return StartCoroutine(Transition(i + 1));
        }

        // 4. Fade-out pixeleado y menu final
        yield return StartCoroutine(PixelFadeToBlack());
        ShowMenu();
        yield return StartCoroutine(FadeOverlayOut(0.6f));
    }

    private void OnNextClicked()
    {
        _nextClicked = true;
    }

    private void ShowCaption(string caption)
    {
        bool has = !string.IsNullOrEmpty(caption);
        _captionPanel.gameObject.SetActive(has);
        if (has) _captionText.text = caption;
    }

    private void SetSprite(Image img, int index)
    {
        Sprite sp = (slides != null && index >= 0 && index < slides.Length) ? slides[index].image : null;
        img.sprite = sp;
        img.enabled = sp != null;
        img.preserveAspect = preserveAspect;
        img.color = Color.white;
    }

    private static void ResetRect(RectTransform rt)
    {
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        rt.anchoredPosition = Vector2.zero;
    }

    // ---------------------------------------------------------------------
    //  BOTON NEXT
    // ---------------------------------------------------------------------

    private IEnumerator PopNext(bool show)
    {
        if (show)
        {
            _nextButton.gameObject.SetActive(true);
            _nextButton.interactable = true;
            _nextFx.pulse = false;
            _nextFx.externalScale = 0f;

            float t = 0f;
            while (t < 0.4f)
            {
                t += Time.unscaledDeltaTime;
                _nextFx.externalScale = EaseOutBack(Mathf.Clamp01(t / 0.4f));
                yield return null;
            }
            _nextFx.externalScale = 1f;
            _nextFx.pulse = true;
        }
        else
        {
            _nextButton.interactable = false;
            _nextFx.pulse = false;

            float t = 0f;
            while (t < 0.2f)
            {
                t += Time.unscaledDeltaTime;
                _nextFx.externalScale = 1f - EaseInCubic(Mathf.Clamp01(t / 0.2f));
                yield return null;
            }
            _nextFx.externalScale = 0f;
            _nextButton.gameObject.SetActive(false);
        }
    }

    // ---------------------------------------------------------------------
    //  TRANSICIONES ENTRE SLIDES
    // ---------------------------------------------------------------------

    private IEnumerator Transition(int next)
    {
        TransitionStyle style = transitionStyle;
        if (style == TransitionStyle.Random)
        {
            // Elige al azar evitando repetir la misma dos veces seguidas
            int pick;
            do { pick = Random.Range(1, 4); } while (pick == _lastStyle);
            _lastStyle = pick;
            style = (TransitionStyle)pick;
        }

        switch (style)
        {
            case TransitionStyle.Blinds: yield return StartCoroutine(TransBlinds(next)); break;
            case TransitionStyle.ZoomSpin: yield return StartCoroutine(TransZoomSpin(next)); break;
            default: yield return StartCoroutine(TransPush(next)); break;
        }
    }

    // Persianas de colores que cubren la pantalla, cambian el slide y se retiran por el lado contrario
    private IEnumerator TransBlinds(int next)
    {
        int n = _strips.Length;
        const float cover = 0.35f;
        const float stagger = 0.025f;
        float total = cover + stagger * (n - 1);

        for (int i = 0; i < n; i++)
        {
            RectTransform rt = _strips[i].rectTransform;
            rt.pivot = new Vector2(0.5f, i % 2 == 0 ? 0f : 1f);
            rt.localScale = new Vector3(1f, 0f, 1f);
            _strips[i].gameObject.SetActive(true);
        }

        // Cubrir
        float t = 0f;
        while (t < total)
        {
            t += Time.unscaledDeltaTime;
            for (int i = 0; i < n; i++)
            {
                float k = Mathf.Clamp01((t - i * stagger) / cover);
                _strips[i].rectTransform.localScale = new Vector3(1f, EaseOutCubic(k), 1f);
            }
            yield return null;
        }

        // Pantalla totalmente cubierta: cambio de slide
        SetSprite(_imgCur, next);
        StartShake(14f, 0.3f);

        // Retirar hacia el lado contrario
        for (int i = 0; i < n; i++)
            _strips[i].rectTransform.pivot = new Vector2(0.5f, i % 2 == 0 ? 1f : 0f);

        t = 0f;
        while (t < total)
        {
            t += Time.unscaledDeltaTime;
            for (int i = 0; i < n; i++)
            {
                float k = Mathf.Clamp01((t - (n - 1 - i) * stagger) / cover);
                _strips[i].rectTransform.localScale = new Vector3(1f, 1f - EaseInCubic(k), 1f);
            }
            yield return null;
        }

        for (int i = 0; i < n; i++) _strips[i].gameObject.SetActive(false);
    }

    // El slide viejo gira y se agranda; el nuevo entra girando con rebote y un destello blanco
    private IEnumerator TransZoomSpin(int next)
    {
        const float dur = 0.75f;
        RectTransform cur = _imgCur.rectTransform;
        RectTransform nxt = _imgNext.rectTransform;

        SetSprite(_imgNext, next);
        _imgNext.color = new Color(1f, 1f, 1f, 0f);
        _imgNext.gameObject.SetActive(true);
        nxt.localScale = Vector3.one * 0.6f;
        nxt.localRotation = Quaternion.Euler(0f, 0f, 12f);

        _flash.color = new Color(1f, 1f, 1f, 0f);
        _flash.gameObject.SetActive(true);

        bool shook = false;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);

            float o = EaseInCubic(k);
            cur.localScale = Vector3.one * Mathf.Lerp(1f, 1.7f, o);
            cur.localRotation = Quaternion.Euler(0f, 0f, -10f * o);

            _imgNext.color = new Color(1f, 1f, 1f, Mathf.Clamp01(k * 3f));
            nxt.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, EaseOutBack(k));
            nxt.localRotation = Quaternion.Euler(0f, 0f, 12f * (1f - EaseOutCubic(k)));

            float f = Mathf.Clamp01(1f - Mathf.Abs(k - 0.35f) / 0.25f) * 0.85f;
            _flash.color = new Color(1f, 1f, 1f, f);

            if (!shook && k >= 0.35f) { shook = true; StartShake(20f, 0.35f); }
            yield return null;
        }

        SetSprite(_imgCur, next);
        ResetRect(cur);
        ResetRect(nxt);
        _imgNext.gameObject.SetActive(false);
        _flash.gameObject.SetActive(false);
    }

    // El slide nuevo entra empujando al viejo, que se encoge e inclina; al aterrizar: sacudida + destello
    private IEnumerator TransPush(int next)
    {
        const float dur = 0.7f;
        float w = _canvasRect.rect.width;
        RectTransform cur = _imgCur.rectTransform;
        RectTransform nxt = _imgNext.rectTransform;

        SetSprite(_imgNext, next);
        _imgNext.gameObject.SetActive(true);
        nxt.anchoredPosition = new Vector2(w, 0f);

        bool landed = false;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            float e = EaseOutCubic(k);

            nxt.anchoredPosition = new Vector2(w * (1f - e), 0f);
            cur.anchoredPosition = new Vector2(-w * 0.45f * e, 0f);
            cur.localScale = Vector3.one * Mathf.Lerp(1f, 0.85f, e);
            cur.localRotation = Quaternion.Euler(0f, 0f, 6f * e);

            if (!landed && k >= 0.85f)
            {
                landed = true;
                StartShake(22f, 0.35f);
                StartCoroutine(FlashPulse(Color.white, 0.5f, 0.25f));
            }
            yield return null;
        }

        SetSprite(_imgCur, next);
        ResetRect(cur);
        ResetRect(nxt);
        _imgNext.gameObject.SetActive(false);
    }

    private void StartShake(float magnitude, float duration)
    {
        if (_shakeRoutine != null) StopCoroutine(_shakeRoutine);
        _shakeRoutine = StartCoroutine(ShakeRoutine(magnitude, duration));
    }

    private IEnumerator ShakeRoutine(float magnitude, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float damp = 1f - Mathf.Clamp01(t / duration);
            _slideRoot.anchoredPosition = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * magnitude * damp;
            yield return null;
        }
        _slideRoot.anchoredPosition = Vector2.zero;
        _shakeRoutine = null;
    }

    private IEnumerator FadeFlash(Color color, float from, float to, float duration)
    {
        _flash.gameObject.SetActive(true);
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
            _flash.color = new Color(color.r, color.g, color.b, a);
            yield return null;
        }
        _flash.color = new Color(color.r, color.g, color.b, to);
        if (to <= 0f) _flash.gameObject.SetActive(false);
    }

    private IEnumerator FlashPulse(Color color, float peak, float duration)
    {
        _flash.gameObject.SetActive(true);
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float a = peak * (1f - Mathf.Clamp01(t / duration));
            _flash.color = new Color(color.r, color.g, color.b, a);
            yield return null;
        }
        _flash.gameObject.SetActive(false);
    }

    // ---------------------------------------------------------------------
    //  FADE-OUT PIXELEADO
    // ---------------------------------------------------------------------

    // Captura la pantalla y la muestra cada vez mas pixelada mientras se oscurece hasta negro.
    // Al terminar deja el overlay negro cubriendo todo.
    private IEnumerator PixelFadeToBlack()
    {
        yield return new WaitForEndOfFrame();

        int w = Screen.width;
        int h = Screen.height;

        // Con mipmaps + filtro trilinear, cada bloque de pixeles sale como el promedio de su zona
        var shot = new Texture2D(w, h, TextureFormat.RGB24, true);
        shot.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        shot.Apply(true);
        shot.filterMode = FilterMode.Trilinear;
        shot.wrapMode = TextureWrapMode.Clamp;

        _pixelOverlay.gameObject.SetActive(true);
        _pixelOverlay.transform.SetAsLastSibling();
        _pixelOverlay.color = Color.white;

        int count = Mathf.Max(1, pixelSizes != null ? pixelSizes.Length : 0);
        float scale = h / 1080f;
        RenderTexture rt = null;
        int lastIdx = -1;

        float t = 0f;
        while (t < pixelFadeDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / pixelFadeDuration);

            int idx = Mathf.Min(count - 1, (int)(k * count));
            if (idx != lastIdx)
            {
                lastIdx = idx;
                int size = (pixelSizes != null && pixelSizes.Length > 0) ? pixelSizes[idx] : 1;
                int px = Mathf.Max(1, Mathf.RoundToInt(size * scale));

                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                rt = RenderTexture.GetTemporary(Mathf.Max(1, w / px), Mathf.Max(1, h / px), 0);
                rt.filterMode = FilterMode.Point;
                Graphics.Blit(shot, rt);
                _pixelOverlay.texture = rt;
            }

            float fade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 1f, k));
            _pixelOverlay.color = Color.Lerp(Color.white, Color.black, fade);
            yield return null;
        }

        // Negro total
        _pixelOverlay.texture = null;
        _pixelOverlay.color = Color.black;
        if (rt != null) RenderTexture.ReleaseTemporary(rt);
        Destroy(shot);
    }

    private IEnumerator FadeOverlayOut(float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            _pixelOverlay.color = new Color(0f, 0f, 0f, 1f - Mathf.Clamp01(t / duration));
            yield return null;
        }
        _pixelOverlay.gameObject.SetActive(false);
    }

    // ---------------------------------------------------------------------
    //  MENU FINAL
    // ---------------------------------------------------------------------

    private void ShowMenu()
    {
        // Oculta todo lo del intro; el overlay negro sigue encima hasta que se desvanezca
        _slideRoot.gameObject.SetActive(false);
        _fxRoot.gameObject.SetActive(false);
        _captionPanel.gameObject.SetActive(false);
        _nextButton.gameObject.SetActive(false);
        _startScreen.gameObject.SetActive(false);

        _menuGroup.alpha = 0f;
        _menuGroup.interactable = true;
        _menuGroup.blocksRaycasts = true;
        _menuGroup.gameObject.SetActive(true);
        StartCoroutine(MenuEntrance());
    }

    private IEnumerator MenuEntrance()
    {
        _menuTitleText.rectTransform.localScale = Vector3.zero;
        for (int j = 0; j < _menuFx.Length; j++) _menuFx[j].externalScale = 0f;

        float t = 0f;
        const float total = 1.3f;
        while (t < total)
        {
            t += Time.unscaledDeltaTime;
            _menuGroup.alpha = Mathf.Clamp01(t / 0.4f);
            _menuTitleText.rectTransform.localScale = Vector3.one * EaseOutBack(Mathf.Clamp01(t / 0.5f));
            for (int j = 0; j < _menuFx.Length; j++)
                _menuFx[j].externalScale = EaseOutBack(Mathf.Clamp01((t - 0.3f - 0.18f * j) / 0.45f));
            yield return null;
        }

        _menuGroup.alpha = 1f;
        _menuTitleText.rectTransform.localScale = Vector3.one;
        for (int j = 0; j < _menuFx.Length; j++) _menuFx[j].externalScale = 1f;
    }

    // Los botones consultan los campos del Inspector al hacer click, no una copia
    // almacenada cuando se creo el Canvas.
    private void OnMenuButtonA() { OnMenuButton(sceneA); }
    private void OnMenuButtonB() { OnMenuButton(sceneB); }

    private void OnMenuButton(string sceneName)
    {
        if (_loading) return;

        sceneName = sceneName == null ? "" : sceneName.Trim();
        if (string.IsNullOrEmpty(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError("IntroMenu: no se puede cargar la escena '" + sceneName +
                "'. Comprueba el nombre EXACTO en Scene A / Scene B y agrega la escena " +
                "a File > Build Settings > Scenes In Build (o Build Profiles en Unity 6).", this);
            return;
        }

        _loading = true;
        // Evita clicks repetidos mientras se ejecuta la transicion.
        if (_menuGroup != null)
        {
            _menuGroup.interactable = false;
            _menuGroup.blocksRaycasts = false;
        }
        StartCoroutine(LoadSceneRoutine(sceneName));
    }

    private IEnumerator LoadSceneRoutine(string sceneName)
    {
        // Mantiene la transicion original antes de cambiar de escena.
        yield return StartCoroutine(PixelFadeToBlack());

        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        if (operation == null)
        {
            Debug.LogError("IntroMenu: fallo al iniciar la carga de '" + sceneName + "'.", this);
            _loading = false;
            if (_menuGroup != null)
            {
                _menuGroup.interactable = true;
                _menuGroup.blocksRaycasts = true;
            }
            yield return StartCoroutine(FadeOverlayOut(0.3f));
            yield break;
        }
        while (!operation.isDone) yield return null;
    }

    // ---------------------------------------------------------------------
    //  CONSTRUCCION DE LA UI
    // ---------------------------------------------------------------------

    private static void EnsureEventSystem()
    {
        EventSystem es;
#if UNITY_2023_1_OR_NEWER
        es = Object.FindFirstObjectByType<EventSystem>();
#else
        es = Object.FindObjectOfType<EventSystem>();
#endif
        if (es == null)
        {
            var go = new GameObject("EventSystem", typeof(EventSystem));
            es = go.GetComponent<EventSystem>();
        }

        // Un EventSystem sin modulo de entrada no procesa clicks en botones UI.
        if (es.GetComponent<BaseInputModule>() == null)
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            var module = es.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            module.AssignDefaultActions();
#else
            es.gameObject.AddComponent<StandaloneInputModule>();
#endif
        }
    }

    private void BuildUI()
    {
        // Canvas
        var canvasGo = new GameObject("IntroMenu Canvas", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        _canvasRect = (RectTransform)canvasGo.transform;

        // 1. Fondo negro
        Stretch(NewImage("Background", _canvasRect, Color.black, false).rectTransform);

        // 2. Slides (dos capas para poder hacer transiciones)
        _slideRoot = NewRect("Slides", _canvasRect);
        Stretch(_slideRoot);
        _imgCur = NewImage("SlideCurrent", _slideRoot, Color.white, false);
        _imgNext = NewImage("SlideNext", _slideRoot, Color.white, false);
        Stretch(_imgCur.rectTransform);
        Stretch(_imgNext.rectTransform);
        _imgNext.gameObject.SetActive(false);

        // 3. Efectos: persianas + destello
        _fxRoot = NewRect("Effects", _canvasRect);
        Stretch(_fxRoot);
        int n = Mathf.Max(4, blindCount);
        _strips = new Image[n];
        for (int i = 0; i < n; i++)
        {
            Image strip = NewImage("Strip" + i, _fxRoot, i % 2 == 0 ? accentA : accentB, false);
            RectTransform rt = strip.rectTransform;
            rt.anchorMin = new Vector2(i / (float)n, 0f);
            rt.anchorMax = new Vector2((i + 1) / (float)n, 1f);
            rt.offsetMin = new Vector2(-1f, 0f); // pequeno solape para que no queden rendijas
            rt.offsetMax = new Vector2(1f, 0f);
            strip.gameObject.SetActive(false);
            _strips[i] = strip;
        }
        _flash = NewImage("Flash", _fxRoot, new Color(1f, 1f, 1f, 0f), false);
        Stretch(_flash.rectTransform);
        _flash.gameObject.SetActive(false);

        // 4. Subtitulo
        Image capBg = NewImage("Caption", _canvasRect, new Color(0f, 0f, 0f, 0.6f), false);
        _captionPanel = capBg.rectTransform;
        _captionPanel.anchorMin = _captionPanel.anchorMax = new Vector2(0.5f, 0f);
        _captionPanel.pivot = new Vector2(0.5f, 0f);
        _captionPanel.sizeDelta = new Vector2(1080f, 130f);
        _captionPanel.anchoredPosition = new Vector2(0f, 50f);
        _captionText = NewText(_captionPanel, "Text", "", 34, Color.white, TextAnchor.MiddleCenter);
        Stretch(_captionText.rectTransform);
        _captionText.rectTransform.offsetMin = new Vector2(24f, 8f);
        _captionText.rectTransform.offsetMax = new Vector2(-24f, -8f);
        _captionPanel.gameObject.SetActive(false);

        // 5. Boton NEXT
        _nextButton = CreateButton(_canvasRect, "NextButton", nextLabel, nextButtonSize, nextButtonSprite, OnNextClicked);
        _nextFx = _nextButton.GetComponent<ButtonFX>();
        RectTransform nrt = (RectTransform)_nextButton.transform;
        nrt.anchorMin = nrt.anchorMax = new Vector2(1f, 0f);
        nrt.pivot = new Vector2(1f, 0f);
        nrt.anchoredPosition = nextButtonOffset;
        _nextButton.gameObject.SetActive(false);

        // 6. Menu final
        Image menuBg = NewImage("FinalMenu", _canvasRect, menuBackgroundColor, true);
        Stretch(menuBg.rectTransform);
        menuBg.sprite = menuBackground != null ? menuBackground : CreateDefaultMenuBackground();
        menuBg.color = Color.white;
        menuBg.preserveAspect = false;
        _menuGroup = menuBg.gameObject.AddComponent<CanvasGroup>();

        _menuTitleText = NewText(menuBg.rectTransform, "Title", menuTitle, 110, Color.white, TextAnchor.MiddleCenter);
        RectTransform trt = _menuTitleText.rectTransform;
        trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
        trt.sizeDelta = new Vector2(1500f, 180f);
        trt.anchoredPosition = new Vector2(0f, 240f);
        var titleOutline = _menuTitleText.gameObject.AddComponent<Outline>();
        titleOutline.effectColor = accentA;
        titleOutline.effectDistance = new Vector2(4f, -4f);

        // Panel centrado para separar las opciones del fondo.
        if (showMenuPanel)
        {
            Image panel = NewImage("MenuButtonsPanel", menuBg.rectTransform, menuPanelColor, false);
            RectTransform panelRt = panel.rectTransform;
            panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(menuButtonSize.x + 100f,
                menuButtonSize.y * 2f + menuButtonSpacing + 90f);
            panelRt.anchoredPosition = new Vector2(0f, -40f);
        }

        // Los sprites de los botones finales SIEMPRE caben en el limite indicado.
        // 'useSpriteNativeSize' solo sigue aplicando a NEXT y START.
        Vector2 menuBtnSize = new Vector2(Mathf.Max(80f, menuButtonSize.x), Mathf.Max(40f, menuButtonSize.y));
        Button btnA = CreateButton(menuBg.rectTransform, "ButtonA", buttonALabel, menuBtnSize, buttonASprite, OnMenuButtonA, true);
        Button btnB = CreateButton(menuBg.rectTransform, "ButtonB", buttonBLabel, menuBtnSize, buttonBSprite, OnMenuButtonB, true);

        // Se apilan segun su altura real (las imagenes pueden tener tamanos distintos)
        float hA = ((RectTransform)btnA.transform).sizeDelta.y;
        float hB = ((RectTransform)btnB.transform).sizeDelta.y;
        float total = hA + menuButtonSpacing + hB;
        const float centerY = -40f;
        ((RectTransform)btnA.transform).anchoredPosition = new Vector2(0f, centerY + total * 0.5f - hA * 0.5f);
        ((RectTransform)btnB.transform).anchoredPosition = new Vector2(0f, centerY - total * 0.5f + hB * 0.5f);
        trt.anchoredPosition = new Vector2(0f, centerY + total * 0.5f + 40f + trt.sizeDelta.y * 0.5f);
        _menuFx = new[] { btnA.GetComponent<ButtonFX>(), btnB.GetComponent<ButtonFX>() };
        menuBg.gameObject.SetActive(false);

        // 7. Pantalla de inicio (click para desbloquear audio)
        _startScreen = NewImage("StartScreen", _canvasRect, Color.black, true);
        Stretch(_startScreen.rectTransform);
        var startBtn = _startScreen.gameObject.AddComponent<Button>();
        startBtn.transition = Selectable.Transition.None;
        startBtn.onClick.AddListener(() => _startClicked = true);
        _startText = NewText(_startScreen.rectTransform, "Label", startLabel, 60, Color.white, TextAnchor.MiddleCenter);
        Stretch(_startText.rectTransform);
        if (startSprite != null)
        {
            _startText.gameObject.SetActive(false);
            _startImage = NewImage("StartImage", _startScreen.rectTransform, Color.white, false);
            _startImage.sprite = startSprite;
            _startImage.preserveAspect = true;
            RectTransform srt = _startImage.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.5f);
            srt.anchoredPosition = Vector2.zero;
            srt.sizeDelta = GetButtonSize(startSprite, new Vector2(1000f, 300f));
        }
        _startScreen.gameObject.SetActive(false);

        // 8. Overlay del fade pixeleado (siempre lo ultimo, bloquea clicks mientras dura)
        var ovRt = NewRect("PixelOverlay", _canvasRect);
        Stretch(ovRt);
        _pixelOverlay = ovRt.gameObject.AddComponent<RawImage>();
        _pixelOverlay.raycastTarget = true;
        ovRt.gameObject.SetActive(false);
    }

    private RectTransform NewRect(string objName, Transform parent)
    {
        var go = new GameObject(objName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private Image NewImage(string objName, Transform parent, Color color, bool raycast)
    {
        RectTransform rt = NewRect(objName, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = raycast;
        return img;
    }

    private Text NewText(Transform parent, string objName, string content, int size, Color color, TextAnchor anchor)
    {
        RectTransform rt = NewRect(objName, parent);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = GetFont();
        t.text = content;
        t.fontSize = size;
        t.fontStyle = FontStyle.Bold;
        t.color = color;
        t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    private Button CreateButton(Transform parent, string objName, string label, Vector2 size,
        Sprite sprite, UnityEngine.Events.UnityAction onClick, bool limitToBox = false)
    {
        bool hasSprite = sprite != null;

        Image bg = NewImage(objName, parent, hasSprite ? Color.white : buttonColor, true);
        RectTransform rt = bg.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);

        if (hasSprite)
        {
            // Boton de imagen: el texto ya viene incluido en el sprite
            bg.sprite = sprite;
            bg.preserveAspect = true;
            rt.sizeDelta = limitToBox ? FitSpriteInside(sprite, size) : GetButtonSize(sprite, size);
        }
        else
        {
            rt.sizeDelta = size;
            var outline = bg.gameObject.AddComponent<Outline>();
            outline.effectColor = accentA;
            outline.effectDistance = new Vector2(5f, -5f);
        }

        var btn = bg.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        btn.onClick.AddListener(onClick);

        bg.gameObject.AddComponent<ButtonFX>();

        if (!hasSprite)
        {
            Text txt = NewText(rt, "Label", label, Mathf.RoundToInt(size.y * 0.42f), buttonTextColor, TextAnchor.MiddleCenter);
            Stretch(txt.rectTransform);
        }
        return btn;
    }

    // Limita un sprite al rectangulo dado sin ampliar imagenes pequenas ni deformarlas.
    private static Vector2 FitSpriteInside(Sprite sprite, Vector2 box)
    {
        Vector2 px = sprite.rect.size;
        if (px.x <= 0f || px.y <= 0f) return box;
        float scale = Mathf.Min(1f, Mathf.Min(box.x / px.x, box.y / px.y));
        return px * scale;
    }

    // Fondo espacial ligero generado por codigo. Se reemplaza al asignar Menu Background.
    private static Sprite CreateDefaultMenuBackground()
    {
        const int w = 512, h = 288;
        Texture2D texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        Color bottom = new Color(0.025f, 0.055f, 0.12f, 1f);
        Color top = new Color(0.09f, 0.14f, 0.25f, 1f);
        for (int y = 0; y < h; y++)
        {
            float v = (float)y / (h - 1);
            for (int x = 0; x < w; x++)
            {
                float u = (float)x / (w - 1);
                float glow = Mathf.Exp(-((u - 0.78f) * (u - 0.78f) * 11f + (v - 0.65f) * (v - 0.65f) * 10f));
                Color c = Color.Lerp(bottom, top, v);
                c += new Color(0.035f, 0.075f, 0.13f, 0f) * glow;
                c.a = 1f;
                texture.SetPixel(x, y, c);
            }
        }
        // Estrellas discretas, deterministas y sin alterar el generador aleatorio del juego.
        for (int i = 0; i < 115; i++)
        {
            int x = (i * 193 + i * i * 17 + 37) % w;
            int y = (i * 79 + i * i * 11 + 23) % h;
            Color c = texture.GetPixel(x, y);
            texture.SetPixel(x, y, Color.Lerp(c, Color.white, i % 5 == 0 ? 0.65f : 0.28f));
        }
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 100f);
    }

    // Tamano de un boton de imagen: el nativo del sprite, o ajustado dentro de 'box' sin deformarlo
    private Vector2 GetButtonSize(Sprite sp, Vector2 box)
    {
        Vector2 px = sp.rect.size;
        if (useSpriteNativeSize) return px * spriteButtonScale;

        float k = Mathf.Min(box.x / px.x, box.y / px.y);
        return px * k;
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

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // ---------------------------------------------------------------------
    //  EASING
    // ---------------------------------------------------------------------

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }

    private static float EaseOutCubic(float t)
    {
        float u = 1f - t;
        return 1f - u * u * u;
    }

    private static float EaseInCubic(float t)
    {
        return t * t * t;
    }

    // ---------------------------------------------------------------------
    //  EFECTO DE BOTONES (hover / presionado / pulso)
    // ---------------------------------------------------------------------

    private class ButtonFX : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public float externalScale = 1f; // lo controlan las animaciones de aparicion
        public bool pulse;               // latido suave mientras espera el click

        private bool _hover, _down;
        private float _cur = 1f;

        private void OnEnable()
        {
            _hover = _down = false;
            _cur = 1f;
        }

        private void Update()
        {
            float target = _down ? 0.94f : (_hover ? 1.08f : 1f);
            _cur = Mathf.Lerp(_cur, target, 1f - Mathf.Exp(-16f * Time.unscaledDeltaTime));
            float p = (pulse && !_hover) ? 1f + 0.04f * Mathf.Sin(Time.unscaledTime * 5f) : 1f;
            transform.localScale = Vector3.one * (externalScale * _cur * p);
        }

        public void OnPointerEnter(PointerEventData e) { _hover = true; }
        public void OnPointerExit(PointerEventData e) { _hover = false; _down = false; }
        public void OnPointerDown(PointerEventData e) { _down = true; }
        public void OnPointerUp(PointerEventData e) { _down = false; }
    }
}
