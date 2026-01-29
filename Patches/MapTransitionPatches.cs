using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

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

                MelonLogger.Msg($"[MapTransition] Found FadeManager: {fadeManagerType.FullName}");

                // Cache Instance property (inherited from SingletonMonoBehaviour<T>)
                instanceProperty = AccessTools.Property(fadeManagerType, "Instance");
                if (instanceProperty == null)
                {
                    // Fallback: search base type hierarchy with FlattenHierarchy
                    instanceProperty = fadeManagerType.BaseType?.GetProperty("Instance",
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                }

                bool hasInstance = instanceProperty != null;
                MelonLogger.Msg($"[MapTransition] Instance property: {(hasInstance ? "found" : "NOT FOUND")}");

                if (!hasInstance)
                {
                    MelonLogger.Warning("[MapTransition] Cannot poll FadeManager without Instance property");
                    return;
                }

                // Cache IsFadeFinish method
                isFadeFinishMethod = AccessTools.Method(fadeManagerType, "IsFadeFinish");
                bool hasFadeFinish = isFadeFinishMethod != null;
                MelonLogger.Msg($"[MapTransition] IsFadeFinish method: {(hasFadeFinish ? "found" : "NOT FOUND")}");

                if (!hasFadeFinish)
                {
                    MelonLogger.Warning("[MapTransition] IsFadeFinish not found - fade detection disabled");
                    return;
                }

                isInitialized = true;

                // Log initial state
                try
                {
                    object instance = instanceProperty.GetValue(null);
                    bool initialState = instance != null && (bool)isFadeFinishMethod.Invoke(instance, null);
                    MelonLogger.Msg($"[MapTransition] Cached reflection initialized - IsFadeFinish={initialState}");
                }
                catch
                {
                    MelonLogger.Msg("[MapTransition] Cached reflection initialized - IsFadeFinish=(no instance yet)");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MapTransition] Error initializing cached reflection: {ex.Message}");
            }
        }

        /// <summary>
        /// Find the FadeManager type via assembly scanning.
        /// The System.Fade namespace maps to Il2CppSystem.Fade in unhollowed assemblies.
        /// </summary>
        private static Type FindFadeManagerType()
        {
            string[] typeNames = new[]
            {
                "Il2CppSystem.Fade.FadeManager",
                "System.Fade.FadeManager"
            };

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var name in typeNames)
                    {
                        var type = asm.GetType(name);
                        if (type != null)
                        {
                            MelonLogger.Msg($"[MapTransition] Found FadeManager in {asm.GetName().Name} as {name}");
                            return type;
                        }
                    }
                }
                catch { }
            }

            // Broader search: look for any type named FadeManager
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in asm.GetTypes())
                    {
                        if (type.Name == "FadeManager" && !type.IsNested)
                        {
                            MelonLogger.Msg($"[MapTransition] Found FadeManager via broad search: {type.FullName} in {asm.GetName().Name}");
                            return type;
                        }
                    }
                }
                catch { }
            }

            return null;
        }
    }
}
