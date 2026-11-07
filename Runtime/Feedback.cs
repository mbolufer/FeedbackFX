using System.Collections;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Events;
#if FEEDBACKFX_SRP
using UnityEngine.Rendering; // Volume : canal post-process, seulement avec URP / HDRP
#endif

namespace FeedbackFX
{
    /// <summary>
    /// Restitution ("feedback") générique et réutilisable, destinée à être branchée sur
    /// n'importe quelle action du jeu : collision, impact, score, apparition d'UI…
    /// Le script déclencheur appelle simplement <c>feedback.Play()</c> ou <c>feedback.Play(vitesse)</c>.
    ///
    /// Se pose sur un GameObject et se référence depuis n'importe quel script déclencheur.
    /// <b>Réutilisable</b> : plusieurs déclencheurs peuvent pointer le même Feedback
    /// (dans ce cas, renseigner des cibles explicites — <c>sineTarget</c>, <c>audioSource</c>…).
    ///
    /// Deux points d'entrée : <see cref="Play(float)"/> pour un événement pondéré par une
    /// vitesse d'impact (m/s, composante normale) — normalisée via <c>minSpeed/maxSpeed</c>
    /// puis passée dans <c>responseCurve</c> pour obtenir un <b>poids</b> — et <see cref="Play()"/>
    /// pour un événement discret sans notion de vitesse (poids = 1). Le poids pondère volume,
    /// vitesse d'animation, amplitude du pulse, intensité du shake, weight du post-process, etc.
    /// </summary>
    public class Feedback : MonoBehaviour
    {
        // ── Pondération par vitesse d'impact ──────────────────────────────────────

        [Tooltip("Vitesse d'impact normale (m/s) correspondant à l'abscisse 0 de la courbe de réponse.")]
        public float minSpeed = 0.5f;
        [Tooltip("Vitesse d'impact normale (m/s) correspondant à l'abscisse 1 de la courbe de réponse.")]
        public float maxSpeed = 12f;
        [Tooltip("Abscisse = vitesse d'impact normalisée [0..1] · Ordonnée = poids appliqué aux feedbacks (peut dépasser 1).")]
        public AnimationCurve responseCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        // ── Animator ─────────────────────────────────────────────────────────────

        public bool useAnimator;
        public Animator animator;
        [Tooltip("Trigger déclenché à chaque Play. Laisser vide pour ne pas déclencher de trigger.")]
        public string triggerParam = "";
        [Tooltip("Float param de l'Animator recevant le poids de vitesse. Laisser vide pour ignorer.")]
        public string speedParam = "";
        [Tooltip("animator.speed = animatorBaseSpeed + poids × ce facteur. 0 = ne pas toucher à animator.speed.")]
        public float speedToAnimatorSpeed = 0f;
        [Tooltip("Vitesse de base de l'Animator quand speedToAnimatorSpeed ≠ 0.")]
        public float animatorBaseSpeed = 1f;

        // ── Pulse sinusoïdal procédural ──────────────────────────────────────────

        public enum SineMode { Scale, LocalPosition, LocalRotation }

        public bool useSinePulse;
        [Tooltip("Transform animé. Vide = le transform de ce composant.")]
        public Transform sineTarget;
        public SineMode sineMode = SineMode.Scale;
        [Tooltip("Axes affectés / pondération par axe.\n" +
                 "• Scale : facteur d'oscillation par axe, ajouté au scale local.\n" +
                 "• LocalPosition : direction du déplacement (normalisée).\n" +
                 "• LocalRotation : poids par axe de la rotation (ex. (0,0,1) = roll seul).")]
        public Vector3 sineAxis = Vector3.one;
        [Tooltip("Amplitude de base, multipliée par le poids de vitesse.\n" +
                 "Scale/LocalPosition : unités locales · LocalRotation : degrés.")]
        public float sineAmplitude = 0.15f;
        [Tooltip("Fréquence de l'oscillation (Hz).")]
        public float sineFrequency = 12f;
        [Tooltip("Durée totale du pulse (secondes).")]
        public float sineDuration = 0.4f;
        [Tooltip("Enveloppe d'amortissement sur la durée (abscisse [0..1] = temps normalisé).")]
        public AnimationCurve sineDamping = AnimationCurve.Linear(0f, 1f, 1f, 0f);

        // ── Son ──────────────────────────────────────────────────────────────────

        public bool useSound;
        public AudioSource audioSource;
        public AudioClip clip;
        [Range(0f, 1f)]
        [Tooltip("Volume de base, multiplié par le poids de vitesse.")]
        public float volume = 1f;
        [Tooltip("Variation de pitch aléatoire ± à chaque Play.")]
        public float pitchJitter = 0.05f;
        [Tooltip("Décalage de pitch ajouté proportionnellement au poids de vitesse.")]
        public float pitchFromSpeed = 0f;

        // ── FX (particules) ──────────────────────────────────────────────────────

        public bool useParticles;
        public enum FxAction { Play, Stop, StopAndClear }

        [System.Serializable]
        public class FxEntry
        {
            [Tooltip("ParticleSystem ciblé (sous-systèmes inclus). Bursts, durée… sont gérés dans le FX lui-même.")]
            public ParticleSystem fx;
            [Tooltip("Play : (re)lance le FX — un FX en cours est relancé sans effacer ses particules vivantes.\n" +
                     "Stop : coupe l'émission, les particules vivantes finissent leur vie.\n" +
                     "StopAndClear : coupe et efface immédiatement toutes les particules.")]
            public FxAction action = FxAction.Play;
        }

        [Tooltip("FX joués ou stoppés à chaque déclenchement, dans l'ordre de la liste.")]
        public FxEntry[] fxEntries = new FxEntry[0];

        // ── Timeline (PlayableDirector) ──────────────────────────────────────────

        public bool useTimelines;
        public enum TimelineAction { Play, Pause, Resume, Stop }

        [System.Serializable]
        public class TimelineEntry
        {
            [Tooltip("PlayableDirector ciblé (doit être actif pour jouer).")]
            public PlayableDirector director;
            [Tooltip("Play : rejoue la timeline depuis le début.\n" +
                     "Pause : met en pause à la position courante.\n" +
                     "Resume : relance depuis la position courante (après une Pause).\n" +
                     "Stop : arrête la timeline et la remet au début.")]
            public TimelineAction action = TimelineAction.Play;
        }

        [Tooltip("Timelines jouées / mises en pause / stoppées à chaque déclenchement, dans l'ordre de la liste. Non pondérées par le poids.")]
        public TimelineEntry[] timelineEntries = new TimelineEntry[0];

        // ── Événements ───────────────────────────────────────────────────────────

        public bool useEvents;
        [Tooltip("Appelé à chaque déclenchement avec le poids [0..1+]. Branche ici ce que les autres canaux ne couvrent pas " +
                 "(glitch VHS, vibration manette, appel d'un script du jeu…).")]
        public UnityEvent<float> onPlay = new UnityEvent<float>();

        // ── Activation d'objets ──────────────────────────────────────────────────

        [System.Serializable]
        public class ObjectActivation
        {
            [Tooltip("GameObject dont l'état actif est forcé au déclenchement.")]
            public GameObject target;
            [Tooltip("Coché = SetActive(true) · Décoché = SetActive(false).")]
            public bool active = true;
        }

        public bool useObjectActivation;
        [Tooltip("Objets activés / désactivés à chaque déclenchement (appliqué avant les autres canaux : un FX sous un objet activé ici peut donc jouer).")]
        public ObjectActivation[] objectActivations = new ObjectActivation[0];

        // ── Shader global ────────────────────────────────────────────────────────

        [System.Serializable]
        public class ShaderFloat
        {
            [Tooltip("Nom de la propriété float globale (entrée « Floats globaux » du ShaderManager).")]
            public string name = "";
            [Tooltip("Valeur cible appliquée au déclenchement.")]
            public float value = 1f;
            [Tooltip("Temps de lerp depuis la valeur globale courante vers la valeur cible (secondes). 0 = immédiat.")]
            public float lerpTime = 0.3f;

            public bool IsValid => !string.IsNullOrEmpty(name);
        }

        public bool useShaderGlobal;
        [Tooltip("ShaderManager dont les floats globaux sont modifiés. Vide = un ShaderManager de la scène.")]
        public ShaderManager shaderManager;
        [Tooltip("Propriétés shader globales float amenées à leur valeur cible à chaque Play, par un lerp depuis leur valeur courante.")]
        public ShaderFloat[] shaderFloats = { new ShaderFloat() };

        // ── Post process (pulse de weight) ──────────────────────────────────────────

        public bool useVolumePulse;
#if FEEDBACKFX_SRP
        [Tooltip("Volume post process dont le weight est animé 0 → poids → 0.")]
        public Volume pulseVolume;
#endif
        [Range(0f, 2f), Tooltip("Durée de la montée du weight de 0 au poids, en secondes.")]
        public float volumeFadeIn = 0.15f;
        [Range(0f, 3f), Tooltip("Durée de la descente du weight vers 0, en secondes.")]
        public float volumeFadeOut = 0.5f;

        // ── Camera shake ─────────────────────────────────────────────────────────

        public bool useCameraShake;
        [Tooltip("Caméra à secouer. Vide = Camera.main.")]
        public Camera shakeCamera;
        [Range(0f, 0.3f), Tooltip("Amplitude du shake en world units, pondérée par le poids.")]
        public float shakeMagnitude = 0.05f;
        [Range(0f, 1f), Tooltip("Durée du shake en secondes.")]
        public float shakeDuration = 0.3f;
        [Range(1f, 60f), Tooltip("Fréquence du bruit Perlin (vitesse d'oscillation).")]
        public float shakeFrequency = 18f;

        // ── Interne ──────────────────────────────────────────────────────────────

        Coroutine _sineRoutine;
        Coroutine _shaderRoutine;
        Coroutine _volumeRoutine;
        Coroutine _shakeRoutine;
        Transform  _shakeCamT;
        Vector3    _shakeOrigin;
        Transform  _sineTf;
        Vector3    _sineBaseScale;
        Vector3    _sineBasePos;
        Quaternion _sineBaseRot;

        // ── API ──────────────────────────────────────────────────────────────────

        /// <summary>Poids [0..1+] issu de la courbe de réponse pour une vitesse d'impact donnée.</summary>
        public float Weight(float impactSpeed)
        {
            float t = maxSpeed > minSpeed ? Mathf.InverseLerp(minSpeed, maxSpeed, impactSpeed) : 0f;
            return responseCurve.Evaluate(t);
        }

        /// <summary>Joue tous les canaux activés à pleine intensité (poids = 1). Pour les actions
        /// discrètes sans notion de vitesse d'impact (score, apparition d'UI…).</summary>
        public void Play() => PlayWeighted(1f);

        /// <summary>Joue tous les canaux activés, pondérés par la vitesse d'impact normale (m/s).</summary>
        public void Play(float impactSpeed) => PlayWeighted(Weight(impactSpeed));

        void PlayWeighted(float w)
        {
            // Activation d'objets (en premier : les canaux suivants peuvent cibler ces objets)
            if (useObjectActivation)
            {
                foreach (var oa in objectActivations)
                    if (oa != null && oa.target != null) oa.target.SetActive(oa.active);
            }

            // Animator
            if (useAnimator && animator != null)
            {
                if (!string.IsNullOrEmpty(triggerParam)) animator.SetTrigger(triggerParam);
                if (!string.IsNullOrEmpty(speedParam))   animator.SetFloat(speedParam, w);
                if (speedToAnimatorSpeed != 0f)          animator.speed = animatorBaseSpeed + w * speedToAnimatorSpeed;
            }

            // Son
            if (useSound && audioSource != null && clip != null)
            {
                audioSource.pitch = 1f + w * pitchFromSpeed + Random.Range(-pitchJitter, pitchJitter);
                audioSource.PlayOneShot(clip, Mathf.Clamp(volume * w, 0f, 2f));
            }

            // FX
            if (useParticles)
            {
                foreach (var e in fxEntries)
                {
                    if (e == null || e.fx == null) continue;
                    switch (e.action)
                    {
                        case FxAction.Play:
                            // Relance : coupe l'émission en cours (particules vivantes conservées) puis
                            // rejoue, pour que les bursts se redéclenchent même si le FX tourne encore.
                            e.fx.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                            e.fx.Play(true);
                            break;
                        case FxAction.Stop:
                            e.fx.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                            break;
                        case FxAction.StopAndClear:
                            e.fx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                            break;
                    }
                }
            }

            // Timelines
            if (useTimelines)
            {
                foreach (var e in timelineEntries)
                {
                    if (e == null || e.director == null) continue;
                    switch (e.action)
                    {
                        case TimelineAction.Play:
                            e.director.Stop();
                            e.director.time = 0;
                            e.director.Play();
                            break;
                        case TimelineAction.Pause:
                            e.director.Pause();
                            break;
                        case TimelineAction.Resume:
                            // Resume ne fait rien sur un director stoppé (graphe détruit) → Play depuis la position courante.
                            if (e.director.state == PlayState.Paused && e.director.playableGraph.IsValid())
                                e.director.Resume();
                            else
                                e.director.Play();
                            break;
                        case TimelineAction.Stop:
                            e.director.Stop();
                            e.director.time = 0;
                            break;
                    }
                }
            }

            // Événements
            if (useEvents) onPlay?.Invoke(w);

            // Pulse sinusoïdal
            if (useSinePulse && isActiveAndEnabled)
            {
                RestoreSine();
                _sineRoutine = StartCoroutine(SinePulse(w));
            }

            // Shader global
            if (useShaderGlobal && shaderFloats != null && shaderFloats.Length > 0 && isActiveAndEnabled)
            {
                if (_shaderRoutine != null) StopCoroutine(_shaderRoutine);
                _shaderRoutine = StartCoroutine(ShaderLerp());
            }

#if FEEDBACKFX_SRP
            // Post process (pulse de weight)
            if (useVolumePulse && pulseVolume != null && isActiveAndEnabled)
            {
                if (_volumeRoutine != null) StopCoroutine(_volumeRoutine);
                _volumeRoutine = StartCoroutine(VolumePulse(w));
            }
#endif

            // Camera shake
            if (useCameraShake && isActiveAndEnabled)
            {
                Camera cam = ResolvedShakeCamera;
                if (cam != null && shakeMagnitude > 0f && shakeDuration > 0f)
                {
                    RestoreShake();
                    _shakeRoutine = StartCoroutine(CameraShake(cam, w));
                }
            }
        }

        /// <summary>Durée (s) du canal actif le plus long : timelines jouées (Play / Resume sur un director actif —
        /// durée totale, position ignorée), pulse, shader, post process, shake, clip son. Sert à attendre la fin d'un
        /// feedback utilisé comme étape de séquence. 0 = instantané.</summary>
        public float Duration
        {
            get
            {
                float d = 0f;
                if (useTimelines && timelineEntries != null)
                    foreach (var e in timelineEntries)
                        if (e != null && e.director != null && e.director.playableAsset != null && e.director.isActiveAndEnabled
                            && (e.action == TimelineAction.Play || e.action == TimelineAction.Resume))
                            d = Mathf.Max(d, (float)e.director.duration);
                if (useSinePulse)                      d = Mathf.Max(d, sineDuration);
                if (useShaderGlobal && shaderFloats != null)
                    foreach (var sf in shaderFloats)
                        if (sf != null && sf.IsValid)  d = Mathf.Max(d, sf.lerpTime);
#if FEEDBACKFX_SRP
                if (useVolumePulse && pulseVolume != null) d = Mathf.Max(d, volumeFadeIn + volumeFadeOut);
#endif
                if (useCameraShake)                    d = Mathf.Max(d, shakeDuration);
                if (useSound && clip != null)          d = Mathf.Max(d, clip.length);
                return d;
            }
        }

        /// <summary>Interrompt le feedback : stoppe les timelines qu'il joue (Play / Resume) et
        /// coupe pulse, shader, post process et shake en cours (valeurs de repos restaurées).</summary>
        public void Stop()
        {
            if (timelineEntries != null)
                foreach (var e in timelineEntries)
                    if (e != null && e.director != null
                        && (e.action == TimelineAction.Play || e.action == TimelineAction.Resume))
                        e.director.Stop();
            StopRoutines();
        }

        /// <summary>Test éditeur : joue le feedback à la vitesse d'impact maximale.</summary>
        [ContextMenu("Tester (vitesse max)")]
        void TestPlayMax() => Play(maxSpeed);

        // ── Coroutines ───────────────────────────────────────────────────────────

        IEnumerator SinePulse(float weight)
        {
            _sineTf        = ResolvedSineTarget;
            _sineBaseScale = _sineTf.localScale;
            _sineBasePos   = _sineTf.localPosition;
            _sineBaseRot   = _sineTf.localRotation;

            float dur = Mathf.Max(0.01f, sineDuration);
            float t   = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                ApplySine(_sineTf, _sineBaseScale, _sineBasePos, _sineBaseRot, SineValue(t, weight));
                yield return null;
            }

            RestoreSine();
            _sineRoutine = null;
        }

        /// <summary>Transform effectivement animé par le pulse : <c>sineTarget</c>, sinon ce transform.</summary>
        public Transform ResolvedSineTarget => sineTarget != null ? sineTarget : transform;

        /// <summary>Valeur du pulse (amplitude × poids × enveloppe) à l'instant <paramref name="t"/> (s).
        /// Partagé avec la preview éditeur (<c>FeedbackEditor</c>).</summary>
        public float SineValue(float t, float weight)
        {
            float dur = Mathf.Max(0.01f, sineDuration);
            float env = sineDamping.Evaluate(Mathf.Clamp01(t / dur));
            return Mathf.Sin(t * sineFrequency * 2f * Mathf.PI) * sineAmplitude * weight * env;
        }

        /// <summary>Applique la valeur <paramref name="s"/> du pulse à <paramref name="tf"/> selon <c>sineMode</c>,
        /// à partir des valeurs de repos fournies.</summary>
        public void ApplySine(Transform tf, Vector3 baseScale, Vector3 basePos, Quaternion baseRot, float s)
        {
            switch (sineMode)
            {
                case SineMode.Scale:
                    tf.localScale = baseScale + Vector3.Scale(sineAxis, Vector3.one * s);
                    break;
                case SineMode.LocalPosition:
                    tf.localPosition = basePos + sineAxis.normalized * s;
                    break;
                case SineMode.LocalRotation:
                    tf.localRotation = baseRot * Quaternion.Euler(sineAxis * s);
                    break;
            }
        }

        void RestoreSine()
        {
            if (_sineRoutine != null) { StopCoroutine(_sineRoutine); _sineRoutine = null; }
            if (_sineTf == null) return;
            switch (sineMode)
            {
                case SineMode.Scale:          _sineTf.localScale    = _sineBaseScale; break;
                case SineMode.LocalPosition: _sineTf.localPosition = _sineBasePos;   break;
                case SineMode.LocalRotation: _sineTf.localRotation = _sineBaseRot;   break;
            }
            _sineTf = null;
        }

        IEnumerator ShaderLerp()
        {
            ShaderManager mgr = ResolveShaderManager();

            // Valeurs de départ = valeurs courantes (un re-déclenchement repart d'où on en est).
            var from = new float[shaderFloats.Length];
            float maxTime = 0f;
            for (int i = 0; i < shaderFloats.Length; i++)
            {
                var sf = shaderFloats[i];
                if (sf == null || !sf.IsValid) continue;
                from[i] = GetShaderFloat(mgr, sf.name);
                maxTime = Mathf.Max(maxTime, sf.lerpTime);
            }

            for (float t = 0f; t < maxTime; t += Time.deltaTime)
            {
                for (int i = 0; i < shaderFloats.Length; i++)
                {
                    var sf = shaderFloats[i];
                    if (sf == null || !sf.IsValid) continue;
                    float k = sf.lerpTime > 0f ? Mathf.Clamp01(t / sf.lerpTime) : 1f;
                    SetShaderFloat(mgr, sf.name, Mathf.Lerp(from[i], sf.value, k));
                }
                yield return null;
            }

            foreach (var sf in shaderFloats)
                if (sf != null && sf.IsValid) SetShaderFloat(mgr, sf.name, sf.value);
            _shaderRoutine = null;
        }

        ShaderManager ResolveShaderManager()
        {
            if (shaderManager == null) shaderManager = FindAnyObjectByType<ShaderManager>();
            return shaderManager;
        }

        // Passe par le ShaderManager quand il gère la propriété : sinon son Update() réécrirait
        // la valeur globale chaque frame avec la sienne.
        static float GetShaderFloat(ShaderManager mgr, string name)
            => mgr != null && mgr.TryGetFloat(name, out float v) ? v : Shader.GetGlobalFloat(name);

        static void SetShaderFloat(ShaderManager mgr, string name, float value)
        {
            if (mgr != null) mgr.SetFloat(name, value);
            else             Shader.SetGlobalFloat(name, value);
        }

#if FEEDBACKFX_SRP
        IEnumerator VolumePulse(float weight)
        {
            float peak = Mathf.Clamp01(weight);

            if (volumeFadeIn > 0f)
            {
                float start = pulseVolume.weight; // repart du weight courant si re-déclenché en cours
                for (float e = 0f; e < volumeFadeIn; e += Time.deltaTime)
                {
                    pulseVolume.weight = Mathf.Lerp(start, peak, e / volumeFadeIn);
                    yield return null;
                }
            }
            pulseVolume.weight = peak;

            if (volumeFadeOut > 0f)
            {
                for (float e = 0f; e < volumeFadeOut; e += Time.deltaTime)
                {
                    pulseVolume.weight = Mathf.Lerp(peak, 0f, e / volumeFadeOut);
                    yield return null;
                }
            }
            pulseVolume.weight = 0f;
            _volumeRoutine = null;
        }
#endif

        IEnumerator CameraShake(Camera cam, float weight)
        {
            Transform camT     = cam.transform;
            Vector3   origin   = camT.localPosition;
            _shakeCamT   = camT;
            _shakeOrigin = origin;
            float     elapsed  = 0f;
            float     seed     = Random.value * 100f; // offset aléatoire dans le bruit Perlin
            float     magnitude = shakeMagnitude * Mathf.Clamp01(weight);

            while (elapsed < shakeDuration)
            {
                camT.localPosition = origin + ShakeOffset(elapsed, seed, magnitude);
                elapsed += Time.deltaTime;
                yield return null;
            }

            camT.localPosition = origin;
            _shakeRoutine = null;
            _shakeCamT    = null;
        }

        /// <summary>Caméra effectivement secouée : <c>shakeCamera</c>, sinon <c>Camera.main</c>.</summary>
        public Camera ResolvedShakeCamera => shakeCamera != null ? shakeCamera : Camera.main;

        /// <summary>Décalage local du shake à l'instant <paramref name="elapsed"/> (s), pour une amplitude
        /// déjà pondérée. Partagé avec la preview éditeur (<c>FeedbackEditor</c>).</summary>
        public Vector3 ShakeOffset(float elapsed, float seed, float magnitude)
        {
            if (shakeDuration <= 0f || elapsed >= shakeDuration) return Vector3.zero;
            float intensity = magnitude * (1f - elapsed / shakeDuration); // enveloppe linéaire
            float tx        = elapsed * shakeFrequency + seed;
            float ox = (Mathf.PerlinNoise(tx,       0f) * 2f - 1f) * intensity;
            float oy = (Mathf.PerlinNoise(tx + 50f, 0f) * 2f - 1f) * intensity;
            return new Vector3(ox, oy, 0f);
        }

        // Coupe un shake en cours en remettant la caméra à sa position d'origine
        // (sinon un re-déclenchement ou un Stop la laisserait décalée).
        void RestoreShake()
        {
            if (_shakeRoutine != null) { StopCoroutine(_shakeRoutine); _shakeRoutine = null; }
            if (_shakeCamT != null) _shakeCamT.localPosition = _shakeOrigin;
            _shakeCamT = null;
        }

        void OnDisable() => StopRoutines();

        void StopRoutines()
        {
            RestoreSine();
            if (_shaderRoutine != null)
            {
                StopCoroutine(_shaderRoutine);
                _shaderRoutine = null;
            }
            if (_volumeRoutine != null)
            {
                StopCoroutine(_volumeRoutine);
                _volumeRoutine = null;
#if FEEDBACKFX_SRP
                if (pulseVolume != null) pulseVolume.weight = 0f;
#endif
            }
            RestoreShake();
        }
    }
}
