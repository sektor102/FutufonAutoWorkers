using System;
using System.Collections;
using System.Reflection;
using MSCLoader;
using UnityEngine;

namespace FutufonAutoWorkerMscModLoader
{
    // MSCLoader 1.4.2 exposes values publicly, but keeps labels/UI handles internal.
    // Update existing controls so saved IDs, values, callbacks and keybindings survive.
    internal static class SettingsLocalization
    {
        private const BindingFlags Internal = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly MethodInfo UpdateName = typeof(ModSetting).GetMethod("UpdateName", Internal);
        private static readonly FieldInfo SettingElement = typeof(ModSetting).GetField("SettingsElement", Internal);
        private static readonly FieldInfo KeyName = typeof(ModKeybind).GetField("Name", Internal);
        private static readonly FieldInfo SliderText = typeof(SettingsSliderInt).GetField("TextValues", Internal);
        private static readonly FieldInfo DropdownItems = typeof(SettingsDropDownList).GetField("ArrayOfItems", Internal);

        internal static void Name(ModSetting setting, string name)
        {
            if (setting != null) UpdateName.Invoke(setting, new object[] { name });
        }

        internal static void Name(SettingsKeybind key, string name)
        {
            if (key != null) KeyName.SetValue(key, name);
        }

        internal static void Slider(SettingsSliderInt setting, string[] labels)
        {
            if (setting == null) return;
            ReplaceLabels(SliderText, setting, labels);
            SliderCaption(setting);
        }

        internal static void SliderCaption(SettingsSliderInt setting)
        {
            if (setting == null) return;
            var labels = SliderText.GetValue(setting) as string[];
            int value = setting.GetValue();
            if (labels != null && value >= 0 && value < labels.Length)
                UiProperty(setting, "value", "text", labels[value]);
        }

        internal static void Dropdown(SettingsDropDownList setting, string[] labels)
        {
            if (setting == null) return;
            ReplaceLabels(DropdownItems, setting, labels);
            var dropdown = UiField(setting, "dropDownList");
            if (dropdown == null) return;
            var items = dropdown.GetType().GetField("Items", Internal | BindingFlags.Public).GetValue(dropdown) as IList;
            for (int i = 0; i < labels.Length && i < items.Count; i++)
                items[i].GetType().GetProperty("Caption").SetValue(items[i], labels[i], null);
        }

        internal static void Checkbox(SettingsCheckBox setting)
        {
            if (setting != null) UiProperty(setting, "checkBox", "isOn", setting.GetValue());
        }

        private static void ReplaceLabels(FieldInfo field, object setting, string[] labels)
        {
            var current = field.GetValue(setting) as string[];
            // Loader slider callbacks capture this array; preserve the reference.
            if (current != null && current.Length == labels.Length) Array.Copy(labels, current, labels.Length);
            else field.SetValue(setting, labels);
        }

        private static object UiField(ModSetting setting, string name)
        {
            var element = SettingElement.GetValue(setting) as UnityEngine.Object;
            if (element == null) return null;
            var field = element.GetType().GetField(name, Internal | BindingFlags.Public);
            var component = field.GetValue(element) as UnityEngine.Object;
            return component == null ? null : component;
        }

        private static void UiProperty(ModSetting setting, string field, string property, object value)
        {
            var component = UiField(setting, field);
            if (component != null) component.GetType().GetProperty(property).SetValue(component, value, null);
        }
    }
}
