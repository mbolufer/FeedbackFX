using UnityEditor;
using UnityEngine;

namespace FeedbackFX
{
    /// <summary>Affiche une entrée FX / Timeline de Feedback sur une seule ligne : [Cible][Action].</summary>
    [CustomPropertyDrawer(typeof(Feedback.FxEntry))]
    [CustomPropertyDrawer(typeof(Feedback.TimelineEntry))]
    public class FxEntryDrawer : PropertyDrawer
    {
        const float ActionWidth = 110f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
            => EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var fx     = property.FindPropertyRelative("fx") ?? property.FindPropertyRelative("director");
            var action = property.FindPropertyRelative("action");

            int indent = EditorGUI.indentLevel;
            Rect content = EditorGUI.IndentedRect(position);
            EditorGUI.indentLevel = 0;

            Rect fxR     = new Rect(content.x, content.y, content.width - ActionWidth - 4f, content.height);
            Rect actionR = new Rect(fxR.xMax + 4f, content.y, ActionWidth, content.height);

            EditorGUI.PropertyField(fxR, fx, GUIContent.none);
            EditorGUI.PropertyField(actionR, action, GUIContent.none);

            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }
    }
}
