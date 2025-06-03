using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [AssociatedEntityExtensionData(typeof(ItemExtensionData))]
    [RequireComponent(typeof(CustomPickup))]
    [RequireComponent(typeof(Entity))]
    [DisallowMultipleComponent]
    public class ItemExtension : EntityExtension
    {
        public ItemExtensionData Data => (ItemExtensionData)extensionData;

        [System.NonSerialized] public CustomPickup pickup;

        private VRCPlayerApi localPlayer;
        private uint localPlayerId;

        public override void OnInstantiate()
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  OnInstantiate");
#endif
            pickup = GetComponent<CustomPickup>();
            pickup.IncrementPreventInteraction();
            localPlayer = Networking.LocalPlayer;
            localPlayerId = (uint)localPlayer.playerId;
        }

        public override void AssociateWithExtensionData()
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  AssociateWithExtensionData");
#endif
            pickup.DecrementPreventInteraction();
            ApplyExtensionData();
        }

        public override void DisassociateFromExtensionDataAndReset(EntityExtension defaultExtension)
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  DisassociateFromExtensionDataAndReset");
#endif
            pickup.IncrementPreventInteraction();
        }

        public override void ApplyExtensionData()
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  ApplyExtensionData");
#endif
            if (Data.attachedToPlayerId == 0u)
                return;
            // TODO: but what if it is already attached? In the case of imports.
            if (Data.attachedToPlayerId == localPlayerId)
                Data.itemSystem.AttachToLocalPlayer(Data);
            else
            {
                pickup.IncrementPreventInteraction();
                if (Data.attachedBoneExists)
                    Data.itemSystem.AttachToRemotePlayer(Data);
            }
        }

        public override void OnPickup()
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  OnPickup");
#endif
            Data.itemSystem.OnLocalPlayerPickup(Data);
        }

        public override void OnDrop()
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  OnDrop");
#endif
            Data.itemSystem.SendDropIA(Data);
            ContinuouslyFlagForMovement = false;
        }

        public override void OnPickupUseDown()
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  OnPickupUseDown");
#endif
        }

        public override void OnPickupUseUp()
        {
#if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  OnPickupUseUp");
#endif
        }

        private bool continuouslyFlagForMovement;
        public bool ContinuouslyFlagForMovement
        {
            get => continuouslyFlagForMovement;
            set
            {
                continuouslyFlagForMovement = value;
                if (value)
                    StartMovementLoop();
            }
        }

        private bool movementLoopIsRunning = false;
        private void StartMovementLoop()
        {
            if (movementLoopIsRunning)
                return;
            movementLoopIsRunning = true;
            MovementLoop();
        }

        public void MovementLoop()
        {
            // TODO: flag position and rotation separately and only if it actually changed.
            entity.FlagForPositionAndRotationChange();
            if (!pickup.isHeld || !continuouslyFlagForMovement)
            {
                movementLoopIsRunning = false;
                continuouslyFlagForMovement = false;
                return;
            }
            SendCustomEventDelayedSeconds(nameof(MovementLoop), 0.1f);
        }
    }
}
