using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace FFIV_ScreenReader.Utils
{
    /// <summary>
    /// Shared utilities for Harmony patching. Eliminates duplicated TryPatch / FindType
    /// methods across SaveLoadPatches, PopupPatches, BattleMessagePatches, and MapTransitionPatches.
    /// </summary>
    public static class PatchHelper
    {
        /// <summary>
        /// Finds a type by its full name across all loaded assemblies.
        /// </summary>
        public static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.FullName == fullName)
                            return type;
                    }
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// Finds a type by its simple (unqualified) name across all loaded assemblies.
        /// Only returns non-nested types.
        /// </summary>
        public static Type FindTypeByName(string simpleName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in asm.GetTypes())
                    {
                        if (type.Name == simpleName && !type.IsNested)
                            return type;
                    }
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// Tries to patch a method with a postfix. Logs a warning on failure.
        /// </summary>
        /// <param name="harmony">The Harmony instance</param>
        /// <param name="targetType">The type containing the method to patch</param>
        /// <param name="methodName">The name of the method to patch</param>
        /// <param name="patchClass">The type containing the postfix method</param>
        /// <param name="postfixName">The name of the postfix method</param>
        /// <param name="logTag">Tag for log messages (e.g., "[SaveLoad]")</param>
        /// <param name="paramTypes">Optional parameter types for method overload resolution</param>
        /// <returns>True if patch was applied successfully</returns>
        public static bool TryPatchPostfix(HarmonyLib.Harmony harmony, Type targetType, string methodName,
            Type patchClass, string postfixName, string logTag, Type[] paramTypes = null)
        {
            try
            {
                var method = paramTypes != null
                    ? AccessTools.Method(targetType, methodName, paramTypes)
                    : AccessTools.Method(targetType, methodName);

                if (method != null)
                {
                    var postfix = patchClass.GetMethod(postfixName, BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                    return true;
                }

                MelonLogger.Warning($"{logTag} {targetType.Name}.{methodName} not found");
                return false;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{logTag} Failed to patch {targetType.Name}.{methodName}: {ex.Message}");
                return false;
            }
        }
    }
}
