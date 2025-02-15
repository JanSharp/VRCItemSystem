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

        private bool actuallyStarted = false;
        private VRCPlayerApi localPlayer;
        private uint localPlayerId;

        private void Start()
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  Start");
            #endif
            ActualStart();
        }

        /// <summary>
        /// <para>Code following an <see cref="Object.Instantiate(Object)"/> call runs before
        /// <see cref="Start"/> runs on the instantiated objects... so we must manually call "start" after
        /// instantiation. So in particular when <see cref="InitFromExtensionData"/> or
        /// <see cref="ItemExtensionData.InitFromExtension"/> run.</para>
        /// </summary>
        public void ActualStart()
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  ActualStart");
            #endif
            if (actuallyStarted)
                return;
            actuallyStarted = true;
            pickup = GetComponent<CustomPickup>();
            pickup.IncrementPreventInteraction();
            localPlayer = Networking.LocalPlayer;
            localPlayerId = (uint)localPlayer.playerId;
        }

        public override void InitFromExtensionData()
        {
            #if ItemSystemDebug
            Debug.Log($"[ItemSystemDebug] ItemExtension  InitFromExtensionData");
            #endif
            ActualStart();
            pickup.DecrementPreventInteraction();
            ApplyExtensionData();
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
