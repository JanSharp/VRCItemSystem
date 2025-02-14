using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ItemExtensionData : EntityExtensionData
    {
        [HideInInspector] [SingletonReference] public BoneAttachmentManager boneAttachment;
        [HideInInspector] [SingletonReference] public ItemSystem itemSystem;

        public override bool SupportsImportExport => true;
        public override uint DataVersion => 0u;
        public override uint LowestSupportedDataVersion => 0u;

        public ItemExtension Extension => (ItemExtension)extension;

        [System.NonSerialized] public uint attachedToPlayerId;
        /// <summary>
        /// <para>Explicit default of <see cref="HumanBodyBones.Head"/>, since we do not control
        /// <see cref="HumanBodyBones"/> values.</para>
        /// </summary>
        [System.NonSerialized] public HumanBodyBones attachedToBone = HumanBodyBones.Head;
        [System.NonSerialized] public Vector3 attachedOffsetVector;
        [System.NonSerialized] public Quaternion attachedOffsetRotation;

        public override void InitFromExtension()
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  InitFromExtension");
            #endif
            // Cannot be held at point, at least as of right now, so just do nothing.
            // Otherwise it would have to read data from the CustomPickup and initialize using that.
        }

        public override void Serialize(bool isExport)
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  Serialize");
            #endif
            lockstep.WriteSmallUInt(attachedToPlayerId);
            if (attachedToPlayerId == 0u)
                return;
            lockstep.WriteSmallInt((int)attachedToBone);
            lockstep.WriteVector3(attachedOffsetVector);
            lockstep.WriteQuaternion(attachedOffsetRotation);
        }

        public override void Deserialize(bool isImport, uint importedDataVersion)
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtensionData  Deserialize");
            #endif
            attachedToPlayerId = lockstep.ReadSmallUInt();
            if (attachedToPlayerId == 0u)
                return;
            attachedToBone = (HumanBodyBones)lockstep.ReadSmallInt();
            attachedOffsetVector = lockstep.ReadVector3();
            attachedOffsetRotation = lockstep.ReadQuaternion();
        }
    }
}
