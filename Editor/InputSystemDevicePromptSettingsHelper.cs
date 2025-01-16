using System.Collections.Generic;
using System.Linq;
using InputSystemActionPrompts.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace InputSystemActionPrompts.Editor
{
    public static class InputSystemDevicePromptSettingsHelper
    {
        [MenuItem("Window/Input System Action Prompts/Create Settings")]
        public static void CreateSettings()
        {
            var settings = ScriptableObject.CreateInstance<InputSystemDevicePromptSettings>();
            // Initialize with all input action assets found in project and packages
            settings.InputActionAssets = GetAllInstances<InputActionAsset>().ToList();
            // Initialize with all prompt assets found in project and packages
            settings.DevicePromptAssets = GetAllInstances<InputDevicePromptData>().ToList();
            settings.DefaultDevicePriority = new List<InputDeviceType>
            {
                InputDeviceType.GamePad,
                InputDeviceType.Keyboard,
                InputDeviceType.Mouse
            };
            settings.OpenTag = '[';
            settings.CloseTag = ']';

            // Ensure a Resources folder exists
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");

            AssetDatabase.CreateAsset(settings, $"Assets/Resources/{InputSystemDevicePromptSettings.SettingsDataFile}.asset");
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Gets all instances of a given type in asset database
        /// </summary>
        private static T[] GetAllInstances<T>() where T : ScriptableObject
        {
            // From here https://answers.unity.com/questions/1425758/how-can-i-find-all-instances-of-a-scriptable-objec.html
            var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}"); //FindAssets uses tags check documentation for more info
            var asset = new T[guids.Length];
            for (var i = 0; i < guids.Length; i++) //probably could get optimized 
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                asset[i] = AssetDatabase.LoadAssetAtPath<T>(path);
            }

            return asset;
        }
    }
}