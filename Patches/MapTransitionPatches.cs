using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFIV_ScreenReader.Utils;

namespace FFIV_ScreenReader.Patches
{
    /// <summary>
    /// Suppresses wall tones during map transitions by polling FadeManager state.
    /// Uses cached reflection to call IsFadeFinish() -
    /// no Harmony patches on FadeManager (avoids IL2CPP trampoline issues with Nullable params).
    /// Polled every 100ms from the wall tone loop.
    /// </summary>
    public static class MapTransitionPatches
    {
        private static bool isInitialized = false;

        // Cached reflection members
        private static PropertyInfo instanceProperty;
        private static MethodInfo isFadeFinishMethod;

        // Throttling for IsScreenFading polling (0.1s interval)
        private static float _lastFadeCheckTime = 0f;
        private static bool _lastFadeResult = false;
        private const float FADE_CHECK_INTERVAL = 0.1f;

        /// <summary>
        /// True while the screen is fading (fade not finished).
        /// Checked by the wall tone loop to suppress tones during transitions.
        /// Polls FadeManager.IsFadeFinish() via cached reflection with 0.1s throttling.
        /// </summary>
        public static bool IsScreenFading
        {
            get
            {
                if (!isInitialized) return false;

                // Throttle polling to reduce reflection overhead
                float currentTime = UnityEngine.Time.time;
                if (currentTime - _lastFadeCheckTime < FADE_CHECK_INTERVAL)
                {
                    return _lastFadeResult;
                }
                _lastFadeCheckTime = currentTime;

                try
                {
                    object instance = instanceProperty.GetValue(null);
                    if (instance == null)
                    {
                        _lastFadeResult = false;
                        return false;
                    }

                    bool isFadeFinish = (bool)isFadeFinishMethod.Invoke(instance, null);
                    _lastFadeResult = !isFadeFinish;
                    return _lastFadeResult;
                }
                catch
                {
                    _lastFadeResult = false;
                    return false;
                }
            }
        }

        /// <summary>
        /// Initializes cached reflection for FadeManager polling.
        /// Harmony parameter kept for API compatibility with the initialization pattern.
        /// </summary>
        public static void Initialize(HarmonyLib.Harmony harmony)
        {
            if (isInitialized)
                return;

            try
            {
                Type fadeManagerType = FindFadeManagerType();
                if (fadeManagerType == null)
                {
                    MelonLogger.Warning("[MapTransition] FadeManager type not found");
                    return;
                }

                // Cache Instance property (inherited from SingletonMonoBehaviour<T>)
                instanceProperty = AccessTools.Property(fadeManagerType, "Instance");
                if (instanceProperty == null)
                {
                    // Fallback: search base type hierarchy with FlattenHierarchy
                    instanceProperty = fadeManagerType.BaseType?.GetProperty("Instance",
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                }

                if (instanceProperty == null)
                {
                    MelonLogger.Warning("[MapTransition] Cannot poll FadeManager without Instance property");
                    return;
                }

                // Cache IsFadeFinish method
                isFadeFinishMethod = AccessTools.Method(fadeManagerType, "IsFadeFinish");

                if (isFadeFinishMethod == null)
                {
                    MelonLogger.Warning("[MapTransition] IsFadeFinish not found - fade detection disabled");
                    return;
                }

                isInitialized = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MapTransition] Error initializing cached reflection: {ex.Message}");
            }
        }

        /// <summary>
        /// Find the FadeManager type via assembly scanning.
        /// Tries specific full names first, then falls back to simple name search.
        /// </summary>
        private static Type FindFadeManagerType()
        {
            return PatchHelper.FindType("Il2CppSystem.Fade.FadeManager")
                ?? PatchHelper.FindType("System.Fade.FadeManager")
                ?? PatchHelper.FindTypeByName("FadeManager");
        }
    }
}
