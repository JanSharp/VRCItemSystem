using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;
using UnityEditor;
using UdonSharpEditor;
using System.Linq;
using System.Collections.Generic;

namespace JanSharp
{
    #if false
    [InitializeOnLoad]
    public static class ItemSyncOnBuild
    {
        static ItemSyncOnBuild() => JanSharp.OnBuildUtil.RegisterType<ItemSync>(OnBuild);

        private static bool OnBuild(ItemSync itemSync)
        {
            SerializedObject itemSyncProxy = new SerializedObject(itemSync);

            VRC_Pickup pickup = itemSync.GetComponent<VRC_Pickup>();
            GameObject updateManagerObj = GameObject.Find("/UpdateManager");
            UpdateManager updateManager = updateManagerObj?.GetComponent<UpdateManager>();
            if (pickup == null)
            {
                Debug.LogError("ItemSync must be on a GameObject with a VRC_Pickup component.", itemSync);
                return false;
            }
            if (updateManager == null)
            {
                Debug.LogError("ItemSync requires a GameObject that must be at the root of the scene"
                    + " with the exact name 'UpdateManager' which has the 'UpdateManager' UdonBehaviour.");
                return false;
            }

            itemSyncProxy.FindProperty("pickup").objectReferenceValue = pickup;
            itemSyncProxy.FindProperty("updateManager").objectReferenceValue = updateManager;
            itemSyncProxy.FindProperty("dummyTransform").objectReferenceValue = updateManagerObj?.transform;

            #if ItemSyncDebug
            itemSyncProxy.FindProperty("debugController").objectReferenceValue
                = GameObject.Find("/DebugController")?.GetComponent<ItemSyncDebugController>();
            #endif

            itemSyncProxy.ApplyModifiedProperties();

            return itemSync != null && itemSync != null;
        }
    }

    [CanEditMultipleObjects]
    [CustomEditor(typeof(ItemSync))]
    public class ItemSyncEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(targets))
                return;
            EditorGUILayout.Space();
            base.OnInspectorGUI(); // draws public/serializable fields
            EditorGUILayout.Space();

            var rigidbodies = targets.Cast<ItemSync>()
                .Select(i => i.GetComponent<Rigidbody>())
                .Where(r => r != null)
                .ToArray();

            bool showButton = false;
            if (rigidbodies.Any(r => r.useGravity))
            {
                EditorGUILayout.LabelField("Rigidbodies using Gravity are not supported by the Item Sync script. They don't break it, "
                    + "but gravity related movement will not sync.", EditorStyles.wordWrappedLabel);
                showButton = true;
            }
            if (rigidbodies.Any(r => !r.isKinematic))
            {
                EditorGUILayout.LabelField("Non Kinematic Rigidbodies are not supported by the Item Sync script. They don't break it, "
                    + "but collision related movement will not sync.", EditorStyles.wordWrappedLabel);
                showButton = true;
            }
            if (showButton && GUILayout.Button(new GUIContent("Configure Rigidbody", "Sets: useGravity = false; isKinematic = true;")))
                ConfigureRigidbodies(rigidbodies);
        }

        public static void ConfigureRigidbodies(Rigidbody[] rigidbodies)
        {
            SerializedObject rigidbodiesProxy = new SerializedObject(rigidbodies);
            rigidbodiesProxy.FindProperty("m_UseGravity").boolValue = false;
            rigidbodiesProxy.FindProperty("m_IsKinematic").boolValue = true;
            rigidbodiesProxy.ApplyModifiedProperties();
        }
    }
    #endif
}
