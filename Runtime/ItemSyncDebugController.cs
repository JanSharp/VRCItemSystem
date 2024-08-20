using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;
using TMPro;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ItemSyncDebugController : UdonSharpBehaviour
    {
        [UdonSynced] private float smallMagnitudeDiff = 0.01f;
        [UdonSynced] private float smallAngleDiff = 7f;
        [UdonSynced] private float consistentOffsetDuration = 0.2f;
        [UdonSynced] private int consistentOffsetFrameCount = 4;
        [UdonSynced] private float interpolationDuration = 0.2f;
        [UdonSynced] private float desktopRotationCheckInterval = 1f;
        [UdonSynced] private float desktopRotationCheckFastInterval = 0.15f;
        [UdonSynced] private float desktopRotationTolerance = 3f;
        [UdonSynced] private int desktopRotationFastFalloff = 10;
        [UdonSynced] private bool vRLocalAttachment = true;
        [UdonSynced] private bool desktopLocalAttachment = true;

        public InputField smallMagnitudeDiffGUI;
        public InputField smallAngleDiffGUI;
        public InputField consistentOffsetDurationGUI;
        public InputField consistentOffsetFrameCountGUI;
        public InputField interpolationDurationGUI;
        public InputField desktopRotationCheckIntervalGUI;
        public InputField desktopRotationCheckFastIntervalGUI;
        public InputField desktopRotationToleranceGUI;
        public InputField desktopRotationFastFalloffGUI;
        public Toggle vRLocalAttachmentGUI;
        public Toggle desktopLocalAttachmentGUI;
        [Space]
        public TextMeshPro itemStatesText;

        private ItemSync[] items;
        private ItemSync[] nonIdleItems;
        #if ItemSyncDebug
        private int itemCount = 0;
        private int nonIdleCount = 0;
        #endif

        public void Start()
        {
            if (items == null)
                items = new ItemSync[128];
            if (nonIdleItems == null)
                nonIdleItems = new ItemSync[128];
            UpdateGUI();
        }

        private void UpdateGUI()
        {
            smallMagnitudeDiffGUI.text = smallMagnitudeDiff.ToString();
            smallAngleDiffGUI.text = smallAngleDiff.ToString();
            consistentOffsetDurationGUI.text = consistentOffsetDuration.ToString();
            consistentOffsetFrameCountGUI.text = consistentOffsetFrameCount.ToString();
            interpolationDurationGUI.text = interpolationDuration.ToString();
            desktopRotationCheckIntervalGUI.text = desktopRotationCheckInterval.ToString();
            desktopRotationCheckFastIntervalGUI.text = desktopRotationCheckFastInterval.ToString();
            desktopRotationToleranceGUI.text = desktopRotationTolerance.ToString();
            desktopRotationFastFalloffGUI.text = desktopRotationFastFalloff.ToString();
            vRLocalAttachmentGUI.SetIsOnWithoutNotify(vRLocalAttachment);
            desktopLocalAttachmentGUI.SetIsOnWithoutNotify(desktopLocalAttachment);
        }

        #if ItemSyncDebug
        private void UpdateItems()
        {
            for (int i = 0; i < itemCount; i++)
            {
                var item = items[i];
                item.InterpolationDuration = interpolationDuration;
            }
        }
        #else
        private void UpdateItems() {}
        #endif

        public void ConfirmInput()
        {
            smallMagnitudeDiff = ReadAsFloat(smallMagnitudeDiffGUI, smallMagnitudeDiff);
            smallAngleDiff = ReadAsFloat(smallAngleDiffGUI, smallAngleDiff);
            consistentOffsetDuration = ReadAsFloat(consistentOffsetDurationGUI, consistentOffsetDuration);
            consistentOffsetFrameCount = ReadAsInt(consistentOffsetFrameCountGUI, consistentOffsetFrameCount);
            interpolationDuration = ReadAsFloat(interpolationDurationGUI, interpolationDuration);
            desktopRotationCheckInterval = ReadAsFloat(desktopRotationCheckIntervalGUI, desktopRotationCheckInterval);
            desktopRotationCheckFastInterval = ReadAsFloat(desktopRotationCheckFastIntervalGUI, desktopRotationCheckFastInterval);
            desktopRotationTolerance = ReadAsFloat(desktopRotationToleranceGUI, desktopRotationTolerance);
            desktopRotationFastFalloff = ReadAsInt(desktopRotationFastFalloffGUI, desktopRotationFastFalloff);
            vRLocalAttachment = vRLocalAttachmentGUI.isOn;
            desktopLocalAttachment = desktopLocalAttachmentGUI.isOn;
            ValuesChanged();
        }

        public override void OnDeserialization()
        {
            UpdateItems();
            UpdateGUI();
        }

        private void ValuesChanged()
        {
            UpdateItems();
            UpdateGUI();
            RequestSerialization();
        }

        private float ReadAsFloat(InputField inputField, float fallback)
        {
            float value;
            if (float.TryParse(inputField.text, out value))
                return value;
            return fallback;
        }

        private int ReadAsInt(InputField inputField, int fallback)
        {
            int value;
            if (int.TryParse(inputField.text, out value))
                return value;
            return fallback;
        }

        #if ItemSyncDebug
        public void Register(ItemSync item)
        {
            if (items == null)
                items = new ItemSync[128];
            if (itemCount == items.Length)
                GrowItems();
            items[itemCount] = item;
            item.debugIndex = itemCount;
            itemCount++;
        }

        public void Deregister(ItemSync item)
        {
            if (items == null)
                items = new ItemSync[128];
            int index = item.debugIndex;
            // move current top into the gap
            itemCount--;
            items[index] = items[itemCount];
            items[index].debugIndex = index;
            items[itemCount] = null;
        }

        private void GrowItems()
        {
            ItemSync[] grownItems = new ItemSync[items.Length * 2];
            for (int i = 0; i < items.Length; i++)
                grownItems[i] = items[i];
            items = grownItems;

            ItemSync[] grownNonIdleItems = new ItemSync[nonIdleItems.Length * 2];
            for (int i = 0; i < nonIdleItems.Length; i++)
                grownNonIdleItems[i] = nonIdleItems[i];
            nonIdleItems = grownNonIdleItems;
        }

        public void RegisterNonIdle(ItemSync item)
        {
            if (nonIdleItems == null)
                nonIdleItems = new ItemSync[128];
            nonIdleItems[nonIdleCount] = item;
            item.debugNonIdleIndex = nonIdleCount;
            nonIdleCount++;
        }

        public void DeregisterNonIdle(ItemSync item)
        {
            if (nonIdleItems == null)
                nonIdleItems = new ItemSync[128];
            int index = item.debugNonIdleIndex;
            // move current top into the gap
            nonIdleCount--;
            nonIdleItems[index] = nonIdleItems[nonIdleCount];
            nonIdleItems[index].debugNonIdleIndex = index;
            nonIdleItems[nonIdleCount] = null;
        }

        public void UpdateItemStatesText()
        {
            string str = null;
            for (int i = 0; i < System.Math.Min(100, nonIdleCount); i++)
            {
                str = str == null ? "" : "\n" + str;
                var item = nonIdleItems[i];
                str = $"'{item.name}', {Networking.GetOwner(item.gameObject).displayName}:   {item.StateToString(item.LocalState)}{str}";
            }
            itemStatesText.text = str;
        }
        #endif
    }
}
