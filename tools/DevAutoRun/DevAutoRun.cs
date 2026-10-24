using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace DevAutoRun
{
    /// <summary>
    /// Dev-only: when Valheim starts with -devautorun, loads the test character and world, runs the probes,
    /// logs the results and quits without saving. Does nothing on a normal launch.
    /// </summary>
    [BepInPlugin(Guid, "Mystical Weapons dev auto-run", "0.1.0")]
    [BepInDependency(MysticalGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class DevAutoRunPlugin : BaseUnityPlugin
    {
        public const string Guid = "omm.valheim.devautorun";
        private const string MysticalGuid = "omm.valheim.mysticalweapons";
        private const string Flag = "-devautorun";

        private static ManualLogSource _log;
        private ConfigEntry<string> _character;
        private ConfigEntry<string> _world;
        private ConfigEntry<float> _settleSeconds;
        private ConfigEntry<float> _stepTimeout;

        private void Awake()
        {
            _log = Logger;
            _character = Config.Bind("AutoRun", "Character", "modtest", "Character file name to load (without .fch).");
            _world = Config.Bind("AutoRun", "World", "Mod-Test", "World name to load (case sensitive).");
            _settleSeconds = Config.Bind("AutoRun", "SettleSeconds", 5f, "Seconds to wait after the player spawns before probing.");
            _stepTimeout = Config.Bind("AutoRun", "StepTimeoutSeconds", 180f, "Give up and quit if the menu or the world takes longer than this.");
            if (!Environment.GetCommandLineArgs().Contains(Flag)) return;
            _log.LogInfo($"DevAutoRun: armed (character {_character.Value}, world {_world.Value})");
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            // Main menu up. The intro cinematic takes nearly 3 minutes, so skip it as a player would.
            var deadline = Time.realtimeSinceStartup + _stepTimeout.Value;
            while (!FejdStartup.instance || !FejdStartup.instance.m_mainMenu.activeInHierarchy)
            {
                if (Time.realtimeSinceStartup > deadline) { Fail("main menu never appeared"); yield break; }
                if (CinematicsManager.s_instance && CinematicsManager.IsStartedPlaying())
                {
                    _log.LogInfo("DevAutoRun: skipping the intro cinematic");
                    CinematicsManager.Stop();
                }
                yield return null;
            }
            yield return new WaitForSecondsRealtime(1f);

            string error = null;
            try { error = StartWorld(FejdStartup.instance); }
            catch (Exception e) { error = e.ToString(); }
            if (error != null) { Fail(error); yield break; }

            deadline = Time.realtimeSinceStartup + _stepTimeout.Value;
            while (!Player.m_localPlayer)
            {
                if (Time.realtimeSinceStartup > deadline) { Fail("player never spawned"); yield break; }
                yield return null;
            }
            _log.LogInfo($"DevAutoRun: spawned, settling {_settleSeconds.Value}s");
            yield return new WaitForSecondsRealtime(_settleSeconds.Value);

            try { Probes.Run(_log); }
            catch (Exception e) { Fail("probe threw " + e); yield break; }
            Quit();
        }

        /// <summary>Does what the menu buttons do: pick the character, then start the world as a closed local game.</summary>
        private string StartWorld(FejdStartup menu)
        {
            const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var type = typeof(FejdStartup);

            type.GetMethod("SetSelectedProfile", any).Invoke(menu, new object[] { _character.Value });
            var profiles = (List<PlayerProfile>)type.GetField("m_profiles", any).GetValue(menu);
            var index = (int)type.GetField("m_profileIndex", any).GetValue(menu);
            if (index < 0 || index >= profiles.Count || profiles[index].GetFilename() != _character.Value)
                return $"character '{_character.Value}' not found";
            menu.OnCharacterStart();

            var world = (World)type.GetMethod("FindWorld", any).Invoke(menu, new object[] { _world.Value });
            if (world == null) return $"world '{_world.Value}' not found";
            type.GetField("m_world", any).SetValue(menu, world);
            menu.m_openServerToggle.isOn = false;
            menu.m_publicServerToggle.isOn = false;
            _log.LogInfo($"DevAutoRun: starting {_world.Value} as {_character.Value}");
            menu.OnWorldStart();
            return null;
        }

        private static void Fail(string reason)
        {
            _log.LogError("DevAutoRun: FAILED " + reason);
            Quit();
        }

        /// <summary>Kills the process instead of Application.Quit so the game doesn't save the world or character.</summary>
        private static void Quit()
        {
            _log.LogInfo("DevAutoRun: done, quitting without saving");
            foreach (var listener in BepInEx.Logging.Logger.Listeners)
                if (listener is DiskLogListener disk) disk.LogWriter?.Flush();
            Process.GetCurrentProcess().Kill();
        }

        /// <summary>The Mystical Weapons item list, read from the loaded plugin so this tool needn't build against it.</summary>
        internal static IEnumerable<object> MysticalItems()
        {
            if (!Chainloader.PluginInfos.TryGetValue(MysticalGuid, out var info) || !info.Instance) return null;
            var field = info.Instance.GetType().GetField("Items", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            return (field?.GetValue(null) as IEnumerable)?.Cast<object>();
        }
    }
}
