using System;
using UnityEngine;

namespace FeedbackFX
{
    /// <summary>
    /// Gère et injecte des valeurs globales (Color, float, Vector3) dans les shaders Unity.
    /// Les propriétés sont appliquées via Shader.SetGlobal* et affectent tous les matériaux
    /// qui les référencent. Fonctionne en mode Play et en mode Editor (ExecuteAlways).
    /// </summary>
    [ExecuteAlways]
    public class ShaderManager : MonoBehaviour
    {
        // ── Types de données ──────────────────────────────────────────────────────

        [Serializable]
        public class ColorEntry
        {
            [Tooltip("Nom exact de la propriété globale dans le shader (ex: _GlobalFogColor)")]
            public string propertyName;
            [ColorUsage(showAlpha: true, hdr: true)]
            public Color value = Color.white;
            [Tooltip("Décocher pour ignorer cette entrée sans la supprimer")]
            public bool enabled = true;
        }

        [Serializable]
        public class FloatEntry
        {
            [Tooltip("Nom exact de la propriété globale dans le shader (ex: _GlobalWind)")]
            public string propertyName;
            public float value;
            [Tooltip("Décocher pour ignorer cette entrée sans la supprimer")]
            public bool enabled = true;
        }

        [Serializable]
        public class VectorEntry
        {
            [Tooltip("Nom exact de la propriété globale dans le shader (ex: _GlobalLightDir)")]
            public string propertyName;
            [Tooltip("Si assigné, la position du GameObject est utilisée comme valeur. Ignore le champ 'value'.")]
            public GameObject source;
            public Vector3 value;
            [Tooltip("Décocher pour ignorer cette entrée sans la supprimer")]
            public bool enabled = true;

            public Vector3 ResolvedValue => source != null ? source.transform.position : value;
        }

        // ── Listes ────────────────────────────────────────────────────────────────

        [Header("Couleurs globales")]
        public ColorEntry[] colors = Array.Empty<ColorEntry>();

        [Header("Floats globaux")]
        public FloatEntry[] floats = Array.Empty<FloatEntry>();

        [Header("Vecteurs globaux (Vector3)")]
        public VectorEntry[] vectors = Array.Empty<VectorEntry>();

        // ── Cycle de vie ──────────────────────────────────────────────────────────

        void OnEnable()  => Apply();
        void OnValidate() => Apply();

        void Update() => Apply();

        // ── API publique ──────────────────────────────────────────────────────────

        /// <summary>Injecte toutes les valeurs activées dans les shaders globaux.</summary>
        [ContextMenu("Appliquer maintenant")]
        public void Apply()
        {
            foreach (var c in colors)
            {
                if (c == null || !c.enabled || string.IsNullOrEmpty(c.propertyName)) continue;
                Shader.SetGlobalColor(c.propertyName, c.value);
            }

            foreach (var f in floats)
            {
                if (f == null || !f.enabled || string.IsNullOrEmpty(f.propertyName)) continue;
                Shader.SetGlobalFloat(f.propertyName, f.value);
            }

            foreach (var v in vectors)
            {
                if (v == null || !v.enabled || string.IsNullOrEmpty(v.propertyName)) continue;
                Shader.SetGlobalVector(v.propertyName, v.ResolvedValue);
            }
        }

        /// <summary>Met à jour une couleur globale par nom et l'applique immédiatement.</summary>
        public void SetColor(string propertyName, Color value)
        {
            foreach (var c in colors)
            {
                if (c != null && c.propertyName == propertyName)
                {
                    c.value   = value;
                    c.enabled = true;
                }
            }
            Shader.SetGlobalColor(propertyName, value);
        }

        /// <summary>Met à jour un float global par nom et l'applique immédiatement.</summary>
        public void SetFloat(string propertyName, float value)
        {
            foreach (var f in floats)
            {
                if (f != null && f.propertyName == propertyName)
                {
                    f.value   = value;
                    f.enabled = true;
                }
            }
            Shader.SetGlobalFloat(propertyName, value);
        }

        /// <summary>Lit la valeur d'un float global géré par ce manager. False si aucune entrée ne porte ce nom.</summary>
        public bool TryGetFloat(string propertyName, out float value)
        {
            foreach (var f in floats)
            {
                if (f != null && f.propertyName == propertyName)
                {
                    value = f.value;
                    return true;
                }
            }
            value = 0f;
            return false;
        }

        /// <summary>Met à jour un vecteur global par nom et l'applique immédiatement.</summary>
        public void SetVector(string propertyName, Vector3 value)
        {
            foreach (var v in vectors)
            {
                if (v != null && v.propertyName == propertyName)
                {
                    v.value   = value;
                    v.enabled = true;
                }
            }
            Shader.SetGlobalVector(propertyName, value);
        }
    }
}
