using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace InputSystemActionPrompts.Runtime
{
    /// <summary>
    /// Enumeration of device type
    /// TODO - Remove and use Input system types more effectively
    /// </summary>
    public enum InputDeviceType
    {
        Mouse,
        Keyboard,
        GamePad,
        Touchscreen
    }

    /// <summary>
    /// Encapsulates a binding map entry
    /// </summary>
    public class ActionBindingMapEntry
    {
        public string BindingPath;
        public bool   IsComposite;
        public bool   IsPartOfComposite;
    }

    public static class InputDevicePromptSystem
    {
        /// <summary>
        /// Map of action paths (eg "Player/Move" to binding map entries eg "Gamepad/leftStick")
        /// </summary>
        private static Dictionary<string, List<ActionBindingMapEntry>> s_ActionBindingMap = new();

        /// <summary>
        /// Map of device names (eg "DualShockGamepadHID") to device prompt data (list of action bindings and sprites)
        /// </summary>
        private static Dictionary<string, InputDevicePromptData> s_DeviceDataBindingMap = new();

        /// <summary>
        /// Currently initialized
        /// </summary>
        private static bool s_Initialized;

        /// <summary>
        /// The settings file
        /// </summary>
        private static InputSystemDevicePromptSettings s_Settings;

        /// <summary>
        /// Currently active device
        /// </summary>
        private static InputDevice s_ActiveDevice;

        /// <summary>
        /// Delegate for when the active device changes
        /// </summary>
        public static Action<InputDevice> OnActiveDeviceChanged = delegate { };

        private static List<InputActionAsset> InputActionAssets = new();

        /// <summary>
        /// Event listener for button presses on input system
        /// </summary>
        private static IDisposable s_EventListener;

        private static InputDevicePromptData s_PlatformDeviceOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeOnLoad()
        {
            s_ActionBindingMap = new();
            s_DeviceDataBindingMap = new();
            s_Settings = null;
            s_Initialized = false;
            s_ActiveDevice = null;
            OnActiveDeviceChanged = delegate { };
            s_EventListener?.Dispose();
            s_EventListener = null;
            s_PlatformDeviceOverride = null;
            InputActionAssets = new List<InputActionAsset>();
        }


        public static void Initialize(InputActionAsset inputActionAsset)
        {
            Initialize(new List<InputActionAsset> { inputActionAsset });
        }

        public static void Initialize(List<InputActionAsset> inputActions)
        {
            InitializeOnLoad();

            InputActionAssets.Clear();
            InputActionAssets = inputActions;

            Debug.Log("Initializing InputDevicePromptSystem");
            s_Settings = InputSystemDevicePromptSettings.GetSettingsFromResources();

            InputActionAssets.AddRange(s_Settings.InputActionAssets);

            Debug.Log($"Loaded {InputActionAssets.Count} InputActionAssets");

            if (s_Settings == null)
            {
                Debug.LogWarning("InputSystemDevicePromptSettings missing");
                return;
            }

            if (!s_Settings.PromptSpriteFormatter.Contains(InputSystemDevicePromptSettings.PromptSpriteFormatterSpritePlaceholder))
            {
                Debug.LogError($"{nameof(InputSystemDevicePromptSettings.PromptSpriteFormatter)} must include {InputSystemDevicePromptSettings.PromptSpriteFormatterSpritePlaceholder} or no sprites will be shown.");
            }

            // We'll want to listen to buttons being pressed on any device
            // in order to dynamically switch device prompts (From description in InputSystem.cs)
            s_EventListener = InputSystem.onAnyButtonPress.Call(OnButtonPressed);

            // Listen to device change. If the active device is disconnected, switch to default
            InputSystem.onDeviceChange += OnDeviceChange;

            BuildBindingMaps();
            FindDefaultDevice();

            GetPlatformDeviceOverride(out s_PlatformDeviceOverride);

            s_Initialized = true;
        }


        public static bool GetPlatformDeviceOverride(out InputDevicePromptData inputDevice)
        {
            if (s_PlatformDeviceOverride != null)
            {
                inputDevice = s_PlatformDeviceOverride;
                return true;
            }

            var currentPlatform = Application.platform;

            foreach (var platformOverride in s_Settings.RuntimePlatformsOverride)
            {
                if (platformOverride.Platform == currentPlatform)
                {
                    // Platform specific override found
                    inputDevice = platformOverride.DevicePromptData;
                    return true;
                }
            }

            inputDevice = null;
            return false;
        }

        /// <summary>
        /// Called on device change
        /// </summary>
        /// <param name="device"></param>
        /// <param name="change"></param>
        private static void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            // If the active device has been disconnected, revert to default device
            if (device != s_ActiveDevice) return;

            if (change is InputDeviceChange.Disconnected or InputDeviceChange.Removed)
            {
                FindDefaultDevice();
                // Notify change
                OnActiveDeviceChanged.Invoke(s_ActiveDevice);
            }
        }

        /// <summary>
        /// Replace tags in a given string with TMPPro strings to insert device prompt sprites
        /// </summary>
        /// <param name="inputText"></param>
        /// <returns></returns>
        public static string InsertPromptSprites(string inputText)
        {
            if (!s_Initialized) return "Waiting for initialization...";
            if (!s_Initialized) return "InputSystemDevicePrompt Settings missing - please create using menu item 'Window/Input System Device Prompts/Create Settings'";

            var foundTags = GetTagList(inputText);
            var replacedText = inputText;
            foreach (var tag in foundTags)
            {
                var replacementTagText = GetActionPathBindingTextSpriteTags(tag);

                //if PromptSpriteFormatter is empty for some reason return the text as if formatter was {SPRITE} (normally)
                var promptSpriteFormatter = s_Settings.PromptSpriteFormatter == "" ? InputSystemDevicePromptSettings.PromptSpriteFormatterSpritePlaceholder : s_Settings.PromptSpriteFormatter;
                //PromptSpriteFormatter in settings uses {SPRITE} as a placeholder for the sprite, convert it to {0} for string.Format
                promptSpriteFormatter = promptSpriteFormatter.Replace(InputSystemDevicePromptSettings.PromptSpriteFormatterSpritePlaceholder, "{0}");
                replacementTagText = string.Format(promptSpriteFormatter, replacementTagText);

                replacedText = replacedText.Replace($"{s_Settings.OpenTag}{tag}{s_Settings.CloseTag}", replacementTagText);
            }

            return replacedText;
        }

        /// <summary>
        /// Gets the first matching sprite (eg DualShock Cross Button Sprite) for the given input tag (eg "Player/Jump")
        /// Currently only supports one sprite, not composite (eg WASD)
        /// </summary>
        /// <param name="inputTag"></param>
        /// <returns></returns>
        public static Sprite GetActionPathBindingSprite(string inputTag)
        {
            if (!s_Initialized) return null;
            var (_, matchingPrompt) = GetActionPathBindingPromptEntries(inputTag);
            return matchingPrompt is { Count: > 0 } ? matchingPrompt[0].PromptSprite : null;
        }

        /// <summary>
        /// Gets the current active device matching sprite in DeviceSpriteEntries list for the given sprite name
        /// </summary>
        /// <param name="spriteName"></param>
        /// <returns></returns>
        public static Sprite GetDeviceSprite(string spriteName)
        {
            if (!s_Initialized) return null;

            InputDevicePromptData validDevice;

            if (s_PlatformDeviceOverride != null)
            {
                validDevice = s_PlatformDeviceOverride;
            }
            else
            {
                if (s_ActiveDevice == null) return null;

                var activeDeviceName = s_ActiveDevice.name;

                if (!s_DeviceDataBindingMap.TryGetValue(activeDeviceName, out var value))
                {
                    Debug.LogError($"MISSING_DEVICE_ENTRIES '{activeDeviceName}'");
                    return null;
                }

                //// search for key in dictionary s_DeviceDataBindingMap that starts with activeDeviceName
                //var matchingDevice = s_DeviceDataBindingMap.FirstOrDefault(x => x.Key.StartsWith(activeDeviceName)).Value;

                validDevice = value;
            }


            var matchingSprite = validDevice.DeviceSpriteEntries.FirstOrDefault((sprite) =>
                string.Equals(sprite.SpriteName, spriteName,
                    StringComparison.CurrentCultureIgnoreCase));

            return matchingSprite?.Sprite;
        }

        /// <summary>
        /// Creates a TextMeshPro formatted string for all matching sprites for a given tag
        /// Supports composite tags, eg WASD by returning all matches for active device (observing order)
        /// </summary>
        /// <param name="inputTag"></param>
        /// <returns></returns>
        private static string GetActionPathBindingTextSpriteTags(string inputTag)
        {
            if (s_PlatformDeviceOverride == null) // not platform override
            {
                if (s_ActiveDevice == null) return "NO_ACTIVE_DEVICE";
                var activeDeviceName = s_ActiveDevice.name;

                if (!s_DeviceDataBindingMap.ContainsKey(activeDeviceName))
                {
                    return $"MISSING_DEVICE_ENTRIES '{activeDeviceName}'";
                }
            }

            var lowerCaseTag = inputTag.ToLower();

            if (!s_ActionBindingMap.ContainsKey(lowerCaseTag))
            {
                return $"MISSING_ACTION {lowerCaseTag}";
            }

            var (validDevice, matchingPrompt) = GetActionPathBindingPromptEntries(inputTag);

            if (matchingPrompt == null || matchingPrompt.Count == 0)
            {
                return $"MISSING_PROMPT '{inputTag}'";
            }

            // Return each
            var outputText = string.Empty;
            foreach (var prompt in matchingPrompt)
            {
                outputText += $"<sprite=\"{validDevice.SpriteAsset.name}\" name=\"{prompt.PromptSprite.name}\" {s_Settings.RichTextTags}>";
            }

            return outputText;
        }

        /// <summary>
        /// Gets all matching prompt entries for a given tag (eg "Player/Jump")
        /// </summary>
        /// <param name="inputTag"></param>
        /// <returns></returns>
        /// <summary>
        /// Gets all matching prompt entries for a given tag (eg "Player/Jump")
        /// </summary>
        /// <param name="inputTag"></param>
        /// <returns></returns>
        private static (InputDevicePromptData, List<ActionBindingPromptEntry>) GetActionPathBindingPromptEntries(string inputTag)
        {
            InputDevicePromptData validDevice;

            var lowerCaseTag = inputTag.ToLower();
            if (!s_ActionBindingMap.ContainsKey(lowerCaseTag))
            {
                Debug.LogError($"Action binding map does not contain key '{lowerCaseTag}'");
                return (null, null);
            }

            if (s_PlatformDeviceOverride != null)
            {
                validDevice = s_PlatformDeviceOverride;
            }
            else
            {
                if (s_ActiveDevice == null)
                {
                    Debug.LogError("No active device is set.");
                    return (null, null);
                }

                if (!s_DeviceDataBindingMap.TryGetValue(s_ActiveDevice.name, out var value))
                {
                    Debug.LogError($"Device data binding map does not contain entries for device '{s_ActiveDevice.name}'");
                    return (null, null);
                }

                validDevice = value;
            }

            var validEntries = new List<ActionBindingPromptEntry>();
            var actionBindings = s_ActionBindingMap[lowerCaseTag];

            foreach (var actionBinding in actionBindings)
            {
                var usage = GetUsageFromBindingPath(actionBinding.BindingPath);
                if (string.IsNullOrEmpty(usage))
                {
                    var matchingPrompt = validDevice.ActionBindingPromptEntries.FirstOrDefault((prompt) =>
                        string.Equals(prompt.ActionBindingPath, actionBinding.BindingPath, StringComparison.CurrentCultureIgnoreCase));

                    if (matchingPrompt != null)
                    {
                        validEntries.Add(matchingPrompt);
                    }
                    else
                    {
                        Debug.LogError($"Missing prompt sprite for binding path '{actionBinding.BindingPath}' on device '{validDevice.name}'");
                    }
                }
            }

            if (validEntries.Count == 0)
            {
                Debug.LogError($"No valid prompt entries found for input tag '{inputTag}'");
                return (validDevice, null);
            }

            return (validDevice, validEntries);
        }


        /// <summary>
        /// Extract the usage from a binding path, eg "*/{Submit}" returns "Submit"
        /// </summary>
        /// <param name="actionBinding"></param>
        /// <returns></returns>
        private static string GetUsageFromBindingPath(string actionBinding)
        {
            return actionBinding.Contains("*/{") ? actionBinding.Substring(3, actionBinding.Length - 4) : string.Empty;
        }


        /// <summary>
        /// Extracts all tags from a given string
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        private static List<string> GetTagList(string input)
        {
            var outputTags = new List<string>();
            for (var i = 0; i < input.Length; i++)
            {
                if (input[i] == s_Settings.OpenTag)
                {
                    var start = i + 1;
                    var end = input.IndexOf(s_Settings.CloseTag, i + 1);
                    var foundTag = input.Substring(start, end - start);
                    outputTags.Add(foundTag);
                }
            }

            return outputTags;
        }


        /// <summary>
        /// Finds default device based on current settings priorities
        /// </summary>
        private static void FindDefaultDevice()
        {
            // When we start up there have been no button presses, so we want to pick the first device
            // that matches the priorities in the settings file

            foreach (var deviceType in s_Settings.DefaultDevicePriority)
            {
                foreach (var device in InputSystem.devices.Where(device => DeviceMatchesType(device, deviceType)))
                {
                    s_ActiveDevice = device;
                    return;
                }
            }
        }

        private static bool DeviceMatchesType(InputDevice device, InputDeviceType type)
        {
            return type switch
            {
                InputDeviceType.Mouse => device is Mouse,
                InputDeviceType.Keyboard => device is Keyboard,
                InputDeviceType.GamePad => device is Gamepad,
                InputDeviceType.Touchscreen => device is Touchscreen,
                _ => false
            };
        }


        /// <summary>
        /// Builds internal map of all actions (eg "Player/Jump" to available binding paths (eg "Gamepad/ButtonSouth")
        /// </summary>
        private static void BuildBindingMaps()
        {
            s_ActionBindingMap = new Dictionary<string, List<ActionBindingMapEntry>>();

            // Build a map of all controls and associated bindings
            foreach (var inputActionAsset in InputActionAssets)
            {
                var allActionMaps = inputActionAsset.actionMaps;
                foreach (var actionMap in allActionMaps)
                {
                    foreach (var binding in actionMap.bindings)
                    {
                        var bindingPath = $"{actionMap.name}/{binding.action}";
                        var bindingPathLower = bindingPath.ToLower();

                        //Debug.Log($"Binding {bindingPathLower} to path {binding.path}");
                        var entry = new ActionBindingMapEntry
                        {
                            BindingPath = binding.effectivePath,
                            IsComposite = binding.isComposite,
                            IsPartOfComposite = binding.isPartOfComposite
                        };
                        if (s_ActionBindingMap.TryGetValue(bindingPathLower, out var value))
                        {
                            value.Add(entry);
                        }
                        else
                        {
                            s_ActionBindingMap.Add(bindingPathLower, new List<ActionBindingMapEntry> { entry });
                        }
                    }
                }
            }


            // Build a map of device name to device data
            foreach (var devicePromptData in s_Settings.DevicePromptAssets)
            {
                foreach (var deviceName in devicePromptData.DeviceNames)
                {
                    if (!s_DeviceDataBindingMap.TryAdd(deviceName, devicePromptData))
                    {
                        Debug.LogWarning(
                            $"Duplicate device name found in InputSystemDevicePromptSettings: {deviceName}. Check your entries");
                    }
                }
            }
        }

        /// <summary>
        /// Called when a button is pressed on any device
        /// </summary>
        /// <param name="button"></param>
        private static void OnButtonPressed(InputControl button)
        {
            if (s_ActiveDevice == button.device) return;
            s_ActiveDevice = button.device;
            OnActiveDeviceChanged.Invoke(s_ActiveDevice);
        }
    }
}