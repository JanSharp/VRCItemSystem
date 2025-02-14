using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [AssociatedEntityExtensionData(typeof(ItemExtensionData))]
    [RequireComponent(typeof(CustomPickup))]
    public class ItemExtension : EntityExtension
    {
        public ItemExtensionData Data => (ItemExtensionData)extensionData;

        [System.NonSerialized] public CustomPickup pickup;

        private VRCPlayerApi localPlayer;
        private uint localPlayerId;

        private void Start()
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  Start");
            #endif
            pickup = GetComponent<CustomPickup>();
            localPlayer = Networking.LocalPlayer;
            localPlayerId = (uint)localPlayer.playerId;
        }

        public override void ApplyExtensionData()
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  ApplyExtensionData");
            #endif
            if (Data.attachedToPlayerId == 0u)
                return;
            if (Data.attachedToPlayerId == localPlayerId)
                Data.itemSystem.AttachToLocalPlayer(Data);
            else
                Data.itemSystem.AttachToRemotePlayer(Data);
        }

        public override void OnPickup()
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  OnPickup");
            #endif
            Data.itemSystem.SendPickupIA(Data);
            // StartMovementLoop();
        }

        public override void OnDrop()
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  OnDrop");
            #endif
            Data.itemSystem.SendDropIA(Data);
            // entity.FlagForMovement();
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
            if (!pickup.isHeld)
            {
                movementLoopIsRunning = false;
                return;
            }
            entity.FlagForPositionAndRotationChange();
            SendCustomEventDelayedSeconds(nameof(MovementLoop), 0.1f);
        }
    }
}
