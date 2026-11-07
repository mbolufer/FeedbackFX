using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FeedbackFX
{
    /// <summary>
    /// Inspecteur de <see cref="Feedback"/> rangé par catégories repliables (Son, FX…).
    /// L'en-tête porte la case use* de la catégorie et reste en surbrillance quand le canal
    /// est actif, même replié (orange si actif mais incomplet). Un résumé s'affiche à droite.
    /// </summary>
    [CustomEditor(typeof(Feedback)), CanEditMultipleObjects]
    public class FeedbackEditor : Editor
    {
        class Category
        {
            public string title;
            public string toggle;               // champ bool use* (null = pas de case, ex. pondération)
            public string[] fields;
            public Func<Feedback, string> summary; // texte affiché à droite de l'en-tête
            public Func<Feedback, string> problem; // non null = canal actif mais incomplet
            public Action<FeedbackEditor> extraGUI; // dessiné sous les champs (boutons de preview…)
        }

        static readonly Category[] Categories =
        {
            new Category {
                title = "Pondération (vitesse d'impact)",
                fields = new[] { "minSpeed", "maxSpeed", "responseCurve" },
                summary = f => $"{f.minSpeed:0.##} → {f.maxSpeed:0.##} m/s",
            },
            new Category {
                title = "Activation d'objets", toggle = "useObjectActivation",
                fields = new[] { "objectActivations" },
                summary = f => Count(f.objectActivations?.Count(o => o != null && o.target != null), "objet"),
                problem = f => f.objectActivations == null || !f.objectActivations.Any(o => o != null && o.target != null)
                    ? "Aucun objet assigné." : null,
            },
            new Category {
                title = "Animator", toggle = "useAnimator",
                fields = new[] { "animator", "triggerParam", "speedParam", "speedToAnimatorSpeed", "animatorBaseSpeed" },
                summary = f => f.animator == null ? "" : string.IsNullOrEmpty(f.triggerParam) ? f.animator.name : $"{f.animator.name} · {f.triggerParam}",
                problem = f => f.animator == null ? "Aucun Animator assigné." : null,
            },
            new Category {
                title = "Son", toggle = "useSound",
                fields = new[] { "audioSource", "clip", "volume", "pitchJitter", "pitchFromSpeed" },
                summary = f => f.clip != null ? f.clip.name : "",
                problem = f => f.audioSource == null ? "Aucune AudioSource assignée."
                             : f.clip == null        ? "Aucun clip assigné." : null,
            },
            new Category {
                title = "FX (ParticleSystem)", toggle = "useParticles",
                fields = new[] { "fxEntries" },
                summary = f => FxSummary(f.fxEntries),
                problem = f => f.fxEntries == null || !f.fxEntries.Any(e => e != null && e.fx != null)
                    ? "Aucun FX assigné." : null,
            },
            new Category {
                title = "Timeline (PlayableDirector)", toggle = "useTimelines",
                fields = new[] { "timelineEntries" },
                summary = f => TimelineSummary(f.timelineEntries),
                problem = f => f.timelineEntries == null || !f.timelineEntries.Any(e => e != null && e.director != null)
                    ? "Aucun PlayableDirector assigné." : null,
            },
            new Category {
                title = "Événements (UnityEvent)", toggle = "useEvents",
                fields = new[] { "onPlay" },
                summary = f => Count(f.onPlay?.GetPersistentEventCount(), "appel"),
                problem = f => f.onPlay == null || f.onPlay.GetPersistentEventCount() == 0 ? "Aucun appel branché." : null,
            },
            new Category {
                title = "Pulse sinusoïdal", toggle = "useSinePulse",
                fields = new[] { "sineTarget", "sineMode", "sineAxis", "sineAmplitude", "sineFrequency", "sineDuration", "sineDamping" },
                summary = f => $"{f.sineMode} · {(f.sineTarget != null ? f.sineTarget.name : "ce transform")}",
                extraGUI = ed => ed.DrawSinePreview(),
            },
            new Category {
                title = "Shader global", toggle = "useShaderGlobal",
                fields = new[] { "shaderManager", "shaderFloats" },
                summary = f => f.shaderFloats == null ? "" : string.Join(" · ", f.shaderFloats.Where(s => s != null && s.IsValid).Select(s => $"{s.name} = {s.value:0.##}")),
                problem = f => f.shaderFloats == null || !f.shaderFloats.Any(s => s != null && s.IsValid)
                    ? "Aucune propriété shader renseignée." : null,
            },
#if FEEDBACKFX_SRP
            new Category {
                title = "Post Process (pulse de weight)", toggle = "useVolumePulse",
                fields = new[] { "pulseVolume", "volumeFadeIn", "volumeFadeOut" },
                summary = f => f.pulseVolume != null ? f.pulseVolume.name : "",
                problem = f => f.pulseVolume == null ? "Aucun Volume assigné." : null,
            },
#endif
            new Category {
                title = "Camera Shake", toggle = "useCameraShake",
                fields = new[] { "shakeCamera", "shakeMagnitude", "shakeDuration", "shakeFrequency" },
                summary = f => f.shakeCamera != null ? f.shakeCamera.name : "Camera.main",
                problem = f => f.ResolvedShakeCamera == null ? "Aucune caméra (ni shakeCamera, ni Camera.main)." : null,
                extraGUI = ed => ed.DrawShakePreview(),
            },
        };

        // ── Previews hors Play (shake, pulse) ────────────────────────────────────
        // Les coroutines ne tournent pas en Edit mode : on rejoue Feedback.ShakeOffset / Feedback.SineValue
        // depuis EditorApplication.update, puis on restaure les valeurs d'origine.

        static bool _hooked;

        static void UpdateHooks()
        {
            bool need = _shakePreviews.Count > 0 || _sinePreviews.Count > 0;
            if (need == _hooked) return;
            _hooked = need;
            if (need)
            {
                EditorApplication.update += TickPreviews;
                AssemblyReloadEvents.beforeAssemblyReload += StopAllPreviews;
                EditorApplication.playModeStateChanged += OnPlayModeChanged;
            }
            else
            {
                EditorApplication.update -= TickPreviews;
                AssemblyReloadEvents.beforeAssemblyReload -= StopAllPreviews;
                EditorApplication.playModeStateChanged -= OnPlayModeChanged;
                if (!Application.isPlaying) UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
        }

        static void TickPreviews()
        {
            TickShakePreviews();
            TickSinePreviews();
            // La Game view ne se redessine pas seule en Edit mode.
            if (!Application.isPlaying) UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        static void StopAllPreviews()
        {
            StopAllShakePreviews();
            StopAllSinePreviews();
        }

        // Restaure avant que la scène soit sérialisée pour le Play (ou rechargée à la sortie).
        static void OnPlayModeChanged(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.ExitingEditMode || s == PlayModeStateChange.ExitingPlayMode)
                StopAllPreviews();
        }

        static void DrawPreviewButtons(string label, string tooltipPlay, string tooltipEdit,
                                       Action play, bool canStop, Action stop)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUI.indentLevel * 15f);
                var content = new GUIContent(label, Application.isPlaying ? tooltipPlay : tooltipEdit);
                if (GUILayout.Button(content, EditorStyles.miniButtonLeft)) play();
                using (new EditorGUI.DisabledScope(!canStop))
                    if (GUILayout.Button("■ Stop", EditorStyles.miniButtonRight, GUILayout.Width(50f)))
                        stop();
            }
        }

        // ─ Pulse sinusoïdal ─

        class SinePreview
        {
            public Feedback fb;
            public Transform tf;
            public Vector3 baseScale, basePos;
            public Quaternion baseRot;
            public double start;
            public float weight;
        }

        static readonly List<SinePreview> _sinePreviews = new List<SinePreview>();

        void DrawSinePreview()
        {
            DrawPreviewButtons("▶ Preview pulse",
                "Joue uniquement le pulse sinusoïdal (poids à vitesse max).",
                "Joue le pulse sinusoïdal dans l'éditeur (poids à vitesse max), transform restauré à la fin.",
                () => { foreach (var t in targets) StartSinePreview((Feedback)t); },
                _sinePreviews.Count > 0, StopAllSinePreviews);
        }

        static void StartSinePreview(Feedback fb)
        {
            Transform tf = fb.ResolvedSineTarget;
            if (tf == null || fb.sineDuration <= 0f) return;

            // Un transform déjà en preview repart de ses vraies valeurs de repos.
            for (int i = _sinePreviews.Count - 1; i >= 0; i--)
                if (_sinePreviews[i].tf == tf) EndSinePreview(i);

            _sinePreviews.Add(new SinePreview
            {
                fb        = fb,
                tf        = tf,
                baseScale = tf.localScale,
                basePos   = tf.localPosition,
                baseRot   = tf.localRotation,
                start     = EditorApplication.timeSinceStartup,
                weight    = fb.Weight(fb.maxSpeed),
            });
            UpdateHooks();
        }

        static void TickSinePreviews()
        {
            double now = EditorApplication.timeSinceStartup;
            for (int i = _sinePreviews.Count - 1; i >= 0; i--)
            {
                var p = _sinePreviews[i];
                if (p.fb == null || p.tf == null) { EndSinePreview(i); continue; }
                float elapsed = (float)(now - p.start);
                if (elapsed >= p.fb.sineDuration) { EndSinePreview(i); continue; }
                p.fb.ApplySine(p.tf, p.baseScale, p.basePos, p.baseRot, p.fb.SineValue(elapsed, p.weight));
            }
        }

        // Restaure les trois canaux : le mode a pu changer pendant la preview.
        static void EndSinePreview(int i)
        {
            var p = _sinePreviews[i];
            if (p.tf != null)
            {
                p.tf.localScale    = p.baseScale;
                p.tf.localPosition = p.basePos;
                p.tf.localRotation = p.baseRot;
            }
            _sinePreviews.RemoveAt(i);
            UpdateHooks();
        }

        static void StopAllSinePreviews()
        {
            for (int i = _sinePreviews.Count - 1; i >= 0; i--) EndSinePreview(i);
        }

        // ─ Camera shake ─

        class ShakePreview
        {
            public Feedback fb;
            public Transform camT;
            public Vector3 origin;
            public double start;
            public float seed, magnitude;
        }

        static readonly List<ShakePreview> _shakePreviews = new List<ShakePreview>();

        void DrawShakePreview()
        {
            DrawPreviewButtons("▶ Preview shake",
                "Joue uniquement le camera shake (poids à vitesse max).",
                "Joue le camera shake dans l'éditeur (poids à vitesse max), caméra restaurée à la fin.",
                () => { foreach (var t in targets) StartShakePreview((Feedback)t); },
                _shakePreviews.Count > 0, StopAllShakePreviews);
        }

        static void StartShakePreview(Feedback fb)
        {
            Camera cam = fb.ResolvedShakeCamera;
            if (cam == null || fb.shakeDuration <= 0f) return;

            // Une caméra déjà en preview repart de sa vraie origine.
            for (int i = _shakePreviews.Count - 1; i >= 0; i--)
                if (_shakePreviews[i].camT == cam.transform) EndShakePreview(i);

            _shakePreviews.Add(new ShakePreview
            {
                fb        = fb,
                camT      = cam.transform,
                origin    = cam.transform.localPosition,
                start     = EditorApplication.timeSinceStartup,
                seed      = UnityEngine.Random.value * 100f,
                magnitude = fb.shakeMagnitude * Mathf.Clamp01(fb.Weight(fb.maxSpeed)),
            });
            UpdateHooks();
        }

        static void TickShakePreviews()
        {
            double now = EditorApplication.timeSinceStartup;
            for (int i = _shakePreviews.Count - 1; i >= 0; i--)
            {
                var p = _shakePreviews[i];
                if (p.fb == null || p.camT == null) { EndShakePreview(i); continue; }
                float elapsed = (float)(now - p.start);
                if (elapsed >= p.fb.shakeDuration) { EndShakePreview(i); continue; }
                p.camT.localPosition = p.origin + p.fb.ShakeOffset(elapsed, p.seed, p.magnitude);
            }
        }

        static void EndShakePreview(int i)
        {
            var p = _shakePreviews[i];
            if (p.camT != null) p.camT.localPosition = p.origin;
            _shakePreviews.RemoveAt(i);
            UpdateHooks();
        }

        static void StopAllShakePreviews()
        {
            for (int i = _shakePreviews.Count - 1; i >= 0; i--) EndShakePreview(i);
        }

        const string PrefPrefix = "FeedbackEditor.fold.";
        const float HeaderHeight = 22f;

        static readonly Color ActiveDark    = new Color(0.20f, 0.45f, 0.32f);
        static readonly Color ActiveLight   = new Color(0.56f, 0.82f, 0.64f);
        static readonly Color WarnDark      = new Color(0.55f, 0.40f, 0.12f);
        static readonly Color WarnLight     = new Color(0.96f, 0.78f, 0.45f);
        static readonly Color InactiveDark  = new Color(0.24f, 0.24f, 0.24f);
        static readonly Color InactiveLight = new Color(0.80f, 0.80f, 0.80f);
        static readonly Color NeutralDark   = new Color(0.28f, 0.30f, 0.36f);
        static readonly Color NeutralLight  = new Color(0.72f, 0.76f, 0.84f);

        static GUIStyle _titleStyle, _summaryStyle;

        static string Count(int? n, string noun)
        {
            int c = n ?? 0;
            return c == 0 ? "" : $"{c} {noun}{(c > 1 && !noun.EndsWith("X") ? "s" : "")}";
        }

        // Ex. « 2 play · 1 stop »
        static string FxSummary(Feedback.FxEntry[] entries)
        {
            if (entries == null) return "";
            var valid = entries.Where(e => e != null && e.fx != null).ToArray();
            int play = valid.Count(e => e.action == Feedback.FxAction.Play);
            int stop = valid.Length - play;
            var parts = new List<string>();
            if (play > 0) parts.Add($"{play} play");
            if (stop > 0) parts.Add($"{stop} stop");
            return string.Join(" · ", parts);
        }

        // Ex. « 1 play · 1 pause »
        static string TimelineSummary(Feedback.TimelineEntry[] entries)
        {
            if (entries == null) return "";
            return string.Join(" · ", entries.Where(e => e != null && e.director != null)
                .GroupBy(e => e.action)
                .Select(g => $"{g.Count()} {g.Key.ToString().ToLowerInvariant()}"));
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            InitStyles();

            DrawToolbar();
            EditorGUILayout.Space(2);

            foreach (var cat in Categories)
                DrawCategory(cat);

            DrawOtherProperties();

            serializedObject.ApplyModifiedProperties();
        }

        // ── Barre d'outils ───────────────────────────────────────────────────────

        void DrawToolbar()
        {
            int active =Categories.Count(c => c.toggle != null && serializedObject.FindProperty(c.toggle).boolValue);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"{active} canal{(active > 1 ? "aux" : "")} actif{(active > 1 ? "s" : "")}", EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Tout déplier", EditorStyles.miniButtonLeft))  SetAllFolds(true);
                if (GUILayout.Button("Tout replier", EditorStyles.miniButtonMid))   SetAllFolds(false);
                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                {
                    var content = new GUIContent("▶ Tester", Application.isPlaying
                        ? "Joue le feedback à vitesse max (poids plein)."
                        : "Disponible en Play uniquement (les pulses utilisent des coroutines).");
                    if (GUILayout.Button(content, EditorStyles.miniButtonRight))
                        foreach (var t in targets) ((Feedback)t).Play(((Feedback)t).maxSpeed);
                }
            }
        }

        static void SetAllFolds(bool expanded)
        {
            foreach (var c in Categories) EditorPrefs.SetBool(PrefPrefix + c.title, expanded);
        }

        // ── Catégorie ────────────────────────────────────────────────────────────

        void DrawCategory(Category cat)
        {
            var fb = (Feedback)target;
            SerializedProperty toggleProp = cat.toggle != null ? serializedObject.FindProperty(cat.toggle) : null;
            bool isActive = toggleProp != null && (toggleProp.boolValue || toggleProp.hasMultipleDifferentValues);
            string problem = isActive ? cat.problem?.Invoke(fb) : null;

            string prefKey = PrefPrefix + cat.title;
            bool expanded = EditorPrefs.GetBool(prefKey, false);

            // ─ En-tête ─
            Rect r = GUILayoutUtility.GetRect(0f, HeaderHeight, GUILayout.ExpandWidth(true));
            bool pro = EditorGUIUtility.isProSkin;
            Color bg = toggleProp == null ? (pro ? NeutralDark : NeutralLight)
                     : !isActive          ? (pro ? InactiveDark : InactiveLight)
                     : problem != null    ? (pro ? WarnDark : WarnLight)
                     :                      (pro ? ActiveDark : ActiveLight);
            EditorGUI.DrawRect(r, bg);
            if (isActive)
                EditorGUI.DrawRect(new Rect(r.x, r.y, 3f, r.height), problem != null ? new Color(1f, 0.7f, 0.2f) : new Color(0.35f, 0.95f, 0.55f));

            Rect foldR  = new Rect(r.x + 6f,  r.y + 3f, 13f, 16f);
            Rect togR   = new Rect(r.x + 20f, r.y + 3f, 16f, 16f);
            float textX = toggleProp != null ? togR.xMax + 4f : foldR.xMax + 4f;
            var titleContent = new GUIContent((problem != null ? "⚠ " : "") + cat.title);
            Rect titleR = new Rect(textX, r.y + 3f, _titleStyle.CalcSize(titleContent).x, 16f);
            Rect sumR   = new Rect(titleR.xMax + 8f, r.y + 3f, Mathf.Max(0f, r.xMax - 8f - (titleR.xMax + 8f)), 16f);

            if (Event.current.type == EventType.Repaint)
                EditorStyles.foldout.Draw(foldR, false, false, expanded, false);

            if (toggleProp != null)
            {
                EditorGUI.showMixedValue = toggleProp.hasMultipleDifferentValues;
                EditorGUI.BeginChangeCheck();
                bool v = EditorGUI.Toggle(togR, toggleProp.boolValue);
                if (EditorGUI.EndChangeCheck()) toggleProp.boolValue = v;
                EditorGUI.showMixedValue = false;
            }

            GUI.Label(titleR, titleContent, _titleStyle);
            // Résumé uniquement pour les canaux actifs (et la pondération) : les canaux éteints restent sobres.
            string summary = toggleProp == null || isActive ? cat.summary?.Invoke(fb) : null;
            if (!string.IsNullOrEmpty(summary))
                GUI.Label(sumR, new GUIContent(summary, summary), _summaryStyle);

            // Clic sur l'en-tête (hors case) → plier / déplier
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition)
                && (toggleProp == null || !togR.Contains(e.mousePosition)))
            {
                expanded = !expanded;
                EditorPrefs.SetBool(prefKey, expanded);
                e.Use();
            }

            // ─ Contenu ─
            if (expanded)
            {
                EditorGUILayout.Space(2);
                EditorGUI.indentLevel++;
                if (problem != null)
                    EditorGUILayout.HelpBox(problem, MessageType.Warning);
                foreach (string name in cat.fields)
                {
                    var p = serializedObject.FindProperty(name);
                    if (p != null) EditorGUILayout.PropertyField(p, true);
                }
                cat.extraGUI?.Invoke(this);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(4);
            }

            EditorGUILayout.Space(1);
        }

        // Champs sérialisés non rangés dans une catégorie (ajouts futurs) : jamais masqués.
        void DrawOtherProperties()
        {
            var known = new HashSet<string> { "m_Script" };
            foreach (var c in Categories)
            {
                if (c.toggle != null) known.Add(c.toggle);
                foreach (var f in c.fields) known.Add(f);
            }

            var it = serializedObject.GetIterator();
            bool header = false;
            for (bool enter = true; it.NextVisible(enter); enter = false)
            {
                if (known.Contains(it.name)) continue;
                if (!header)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Autres", EditorStyles.boldLabel);
                    header = true;
                }
                EditorGUILayout.PropertyField(it, true);
            }
        }

        static void InitStyles()
        {
            if (_titleStyle != null) return;
            _titleStyle = new GUIStyle(EditorStyles.boldLabel);
            _summaryStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                clipping  = TextClipping.Clip,
            };
            _summaryStyle.normal.textColor = EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.6f)
                : new Color(0f, 0f, 0f, 0.6f);
        }
    }
}
