using UdonSharpEditor;
using UnityEditor;

namespace JanSharp
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(ItemSystem))]
    public class ItemSystemEditor : Editor
    {
        private static bool internalFoldedOut = false;

        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(targets))
                return;

            if (internalFoldedOut = EditorGUILayout.Foldout(internalFoldedOut, "Internal", toggleOnLabelClick: true))
            {
                serializedObject.Update();
                DrawPropertiesExcluding(serializedObject, "m_Script");
                serializedObject.ApplyModifiedProperties();
            }
        }
    }
}
