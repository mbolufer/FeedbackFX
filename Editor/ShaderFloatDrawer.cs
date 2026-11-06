using UnityEditor;
using UnityEngine;

namespace FeedbackFX
{
    /// <summary>Affiche une propriété shader globale de Feedback sur une seule ligne : [Nom] = [valeur] [temps de lerp] s.</summary>
    [CustomPropertyDrawer(typeof(Feedback.ShaderFloat))]
    public class ShaderFloatDrawer : PropertyDrawer
    {
        const float NumWidth = 50f;
        const float TagWidth = 14f;
        const float Gap      = 4f;

        static readonly GUIContent ValueTag = new GUIContent("=", "Valeur cible appliquée au déclenchement.");
        static readonly GUIContent TimeTag  = new GUIContent("s", "Temps de lerp vers la valeur cible (secondes). 0 = immédiat.");

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
            => EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var name     = property.FindPropertyRelative("name");
            var value    = property.FindPropertyRelative("value");
            var lerpTime = property.FindPropertyRelative("lerpTime");

            int indent = EditorGUI.indentLevel;
            Rect c = EditorGUI.IndentedRect(position);
            EditorGUI.indentLevel = 0;

            float fixedW = 2f * (TagWidth + NumWidth) + 2f * Gap;
            Rect nameR  = new Rect(c.x, c.y, Mathf.Max(40f, c.width - fixedW), c.height);
            Rect valTR  = new Rect(nameR.xMax + Gap, c.y, TagWidth, c.height);
            Rect valR   = new Rect(valTR.xMax, c.y, NumWidth, c.height);
            Rect timeR  = new Rect(valR.xMax + Gap, c.y, NumWidth, c.height);
            Rect timeTR = new Rect(timeR.xMax, c.y, TagWidth, c.height);

            EditorGUI.PropertyField(nameR, name, GUIContent.none);
            EditorGUI.LabelField(valTR, ValueTag, EditorStyles.miniLabel);
            EditorGUI.PropertyField(valR, value, GUIContent.none);
            EditorGUI.PropertyField(timeR, lerpTime, GUIContent.none);
            EditorGUI.LabelField(timeTR, TimeTag, EditorStyles.miniLabel);

            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }
    }
}
