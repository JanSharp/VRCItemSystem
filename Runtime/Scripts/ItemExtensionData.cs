using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ItemExtensionData : EntityExtensionData
    {
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
            // The only way for the pickup to be held by the player at this point is through another system
            // forcing it into their hand.
            // The item system should handle this case, however for now it does not. It should use the current
            // pickup data and initialize from that, though I'd have to think about what that implies for
            // syncing.
            Extension.ActualStart();
            Extension.pickup.DecrementPreventInteraction();
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
