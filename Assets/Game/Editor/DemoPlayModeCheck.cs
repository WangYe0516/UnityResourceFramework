using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ResourceFramework
{
    // SessionState survives the domain reload when entering Play Mode.
    [InitializeOnLoad]
    public static class DemoPlayModeCheck
    {
        private const string ActiveKey = "ResourceFramework.PlayModeCheck.Active";
        private const string DeadlineKey = "ResourceFramework.PlayModeCheck.Deadline";

        static DemoPlayModeCheck() { EditorApplication.update += Check; }

        // Batch invocation must omit -quit; this check exits after the scene's Start has run.
        public static void Run()
        {
            ConfigImportMenu.BatchImportAndCreateDemo();
            EditorSceneManager.OpenScene("Assets/Scenes/DemoScene.unity");
            SessionState.SetString(DeadlineKey, DateTime.UtcNow.AddMinutes(2).Ticks.ToString());
            SessionState.SetBool(ActiveKey, true);
            EditorApplication.EnterPlaymode();
        }

        private static void Check()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            long deadline;
            if (!long.TryParse(SessionState.GetString(DeadlineKey, "0"), out deadline) || DateTime.UtcNow.Ticks > deadline)
            { Finish(false, "Scene did not finish within two minutes."); return; }
            if (!EditorApplication.isPlaying) return;
            var demo = Resources.FindObjectsOfTypeAll<DemoBootstrap>().FirstOrDefault(item => item.gameObject.scene.IsValid());
            if (demo == null || string.IsNullOrEmpty(demo.lastReport)) return;
            if (demo.lastReport.Contains("FAIL:")) Finish(false, demo.lastReport);
            else if (demo.lastReport.Contains("PASS: herb=9, water=3, potion=3, ticket=1, actionPoints=6."))
                Finish(true, "DemoScene Start executed successfully in Play Mode.");
        }

        private static void Finish(bool passed, string message)
        {
            SessionState.SetBool(ActiveKey, false);
            if (passed) Debug.Log("[Play Mode Validation] PASS: " + message);
            else Debug.LogError("[Play Mode Validation] FAIL: " + message);
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
