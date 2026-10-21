using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace StellarisClone.Core
{
    /// <summary>
    /// Достижения VK Play через эмуляцию Steam API (так требует документация VK Play):
    /// в папку с игрой кладётся steam_api64.dll из кабинета разработчика VK Play, а игра вызывает
    /// обычные функции ISteamUserStats — SetAchievement и StoreStats.
    ///
    /// Библиотека подгружается динамически (LoadLibrary + GetProcAddress), поэтому:
    ///   • игра собирается и работает без неё — тогда достижения просто копятся локально;
    ///   • подходит библиотека любой версии SDK: имена функций ищутся по нескольким вариантам.
    /// Работает только в собранной игре под Windows; в редакторе и на Mac — локальный режим.
    /// </summary>
    public static class VkPlayStats
    {
        public enum State { Off, NoLibrary, InitFailed, Ready }

        public static State Status { get; private set; } = State.Off;
        public static bool Ready => Status == State.Ready;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string path);

        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool BoolFn();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int InitFlatFn(IntPtr errMsg);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void VoidFn();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr PtrFn();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool SelfFn(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool SelfNameFn(IntPtr self, [MarshalAs(UnmanagedType.LPStr)] string name);

        private static IntPtr s_lib, s_stats;
        private static VoidFn s_runCallbacks, s_shutdown;
        private static SelfFn s_requestStats, s_storeStats;
        private static SelfNameFn s_setAchievement;
        private static bool s_statsRequested;

        private static T Fn<T>(string name) where T : Delegate
        {
            var p = GetProcAddress(s_lib, name);
            return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(p);
        }
#endif

        /// <summary>Попробовать подключиться к VK Play. Без библиотеки или вне лаунчера — тихо остаётся в локальном режиме.</summary>
        public static void Init()
        {
            if (Status != State.Off) return;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            try
            {
                string root = Path.GetDirectoryName(Application.dataPath);   // папка с Astropolity.exe
                string[] candidates =
                {
                    Path.Combine(root ?? "", "steam_api64.dll"),
                    Path.Combine(Application.dataPath, "Plugins", "x86_64", "steam_api64.dll"),
                    Path.Combine(Application.dataPath, "Plugins", "steam_api64.dll"),
                };
                foreach (var c in candidates)
                {
                    if (!File.Exists(c)) continue;
                    s_lib = LoadLibraryW(c);
                    if (s_lib != IntPtr.Zero) break;
                }
                if (s_lib == IntPtr.Zero) { Status = State.NoLibrary; return; }

                // VK Play перечисляет среди поддерживаемых SteamAPI_Init (true — успех) — пробуем её первой;
                // в библиотеках, где её нет, — SteamAPI_InitFlat из новых SDK (0 — успех)
                bool ok = false;
                var init = Fn<BoolFn>("SteamAPI_Init");
                if (init != null) ok = init();
                else
                {
                    var initFlat = Fn<InitFlatFn>("SteamAPI_InitFlat");
                    if (initFlat != null)
                    {
                        IntPtr err = Marshal.AllocHGlobal(1024);
                        try { ok = initFlat(err) == 0; }
                        finally { Marshal.FreeHGlobal(err); }
                    }
                }
                if (!ok) { Status = State.InitFailed; return; }

                foreach (var v in new[] { "SteamAPI_SteamUserStats_v013", "SteamAPI_SteamUserStats_v012", "SteamAPI_SteamUserStats_v011" })
                {
                    var get = Fn<PtrFn>(v);
                    if (get == null) continue;
                    s_stats = get();
                    if (s_stats != IntPtr.Zero) break;
                }
                s_setAchievement = Fn<SelfNameFn>("SteamAPI_ISteamUserStats_SetAchievement");
                s_storeStats = Fn<SelfFn>("SteamAPI_ISteamUserStats_StoreStats");
                s_requestStats = Fn<SelfFn>("SteamAPI_ISteamUserStats_RequestCurrentStats");   // в новых SDK её нет — статистика приходит сама
                s_runCallbacks = Fn<VoidFn>("SteamAPI_RunCallbacks");
                s_shutdown = Fn<VoidFn>("SteamAPI_Shutdown");
                if (s_stats == IntPtr.Zero || s_setAchievement == null || s_storeStats == null) { Status = State.InitFailed; return; }

                Status = State.Ready;
                RequestStats();
                Application.quitting += Shutdown;
                Debug.Log("[VK Play] Достижения подключены");
            }
            catch (Exception e)
            {
                Status = State.InitFailed;
                Debug.LogWarning("[VK Play] Достижения недоступны: " + e.Message);
            }
#else
            Status = State.NoLibrary;
#endif
        }

        private static void RequestStats()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (s_statsRequested || !Ready) return;
            s_statsRequested = true;
            try { s_requestStats?.Invoke(s_stats); } catch (Exception e) { Debug.LogWarning("[VK Play] " + e.Message); }
#endif
        }

        /// <summary>Обработка ответов сервера; вызывать регулярно.</summary>
        public static void Pump()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (!Ready) return;
            try { s_runCallbacks?.Invoke(); } catch (Exception) { }
#endif
        }

        /// <summary>
        /// Открыть достижение на VK Play. false — пока нельзя (статистика ещё не пришла, нет связи):
        /// тогда вызывающий повторит попытку позже.
        /// </summary>
        public static bool TryUnlock(string apiName)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (!Ready || string.IsNullOrEmpty(apiName)) return false;
            try { return s_setAchievement(s_stats, apiName); }
            catch (Exception e) { Debug.LogWarning("[VK Play] " + e.Message); return false; }
#else
            return false;
#endif
        }

        /// <summary>Отправить открытые достижения на сервер (показывает уведомление VK Play).</summary>
        public static bool Store()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (!Ready) return false;
            try { return s_storeStats(s_stats); }
            catch (Exception e) { Debug.LogWarning("[VK Play] " + e.Message); return false; }
#else
            return false;
#endif
        }

        private static void Shutdown()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (!Ready) return;
            try { s_storeStats?.Invoke(s_stats); s_shutdown?.Invoke(); } catch (Exception) { }
            Status = State.Off;
#endif
        }
    }
}
