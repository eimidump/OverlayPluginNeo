﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows.Forms;
using RainbowMage.OverlayPlugin.NetworkProcessors;

namespace RainbowMage.OverlayPlugin
{
    class OverlayHider
    {
        private bool gameActive = true;
        private bool inCutscene = false;
        private bool inCombat = false;
        private IPluginConfig config;
        private ILogger logger;
        private PluginMain main;
        private int bnsPid = -1;
        private Timer focusTimer;

        public OverlayHider(TinyIoCContainer container)
        {
            this.config = container.Resolve<IPluginConfig>();
            this.logger = container.Resolve<ILogger>();
            this.main = container.Resolve<PluginMain>();

            container.Resolve<NativeMethods>().ActiveWindowChanged += ActiveWindowChangedHandler;

            try
            {
                GetNeoProcess();
            } catch (Exception ex)
            {
                logger.Log(LogLevel.Error, "Failed to register process watcher for B&SNeo; this is only an issue if you're playing B&SNeo. As a consequence, OverlayPlugin won't be able to hide overlays if you're not in-game.");
                logger.Log(LogLevel.Error, "Details: " + ex.ToString());
            }

            focusTimer = new Timer();
            focusTimer.Tick += (o, e) => ActiveWindowChangedHandler(this, IntPtr.Zero);
            focusTimer.Interval = 10000;  // 10 seconds
            focusTimer.Start();
        }

        private void GetNeoProcess()
        {
            var processes = Process.GetProcessesByName("BNSR");
            logger.Log(LogLevel.Debug, $"Found {processes.Length} B&SNeo processes.");
            bnsPid = processes.Length > 0 ? processes[0].Id : -1;
        }

        public void UpdateOverlays()
        {
            if (!config.HideOverlaysWhenNotActive)
                gameActive = true;

            if (!config.HideOverlayDuringCutscene)
                inCutscene = false;

            try
            {
                foreach (var overlay in main.Overlays)
                {
                    if (overlay.Config.IsVisible)
                    {
                        overlay.Visible = gameActive && !inCutscene && (!overlay.Config.HideOutOfCombat || inCombat);
                    }
                }
            } catch (Exception ex)
            {
                logger.Log(LogLevel.Error, $"OverlayHider: Failed to update overlays: {ex}");
            }
        }

        private void ActiveWindowChangedHandler(object sender, IntPtr changedWindow)
        {
            if (!config.HideOverlaysWhenNotActive) return;
            try
            {
                try
                {
                    NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out uint pid);

                    if (pid == 0)
                        return;

                    if (bnsPid != -1)
                    {
                        gameActive = pid == bnsPid || pid == Process.GetCurrentProcess().Id;
                    } else
                    {
                        var exePath = Process.GetProcessById((int)pid).MainModule.FileName;
                        var fileName = Path.GetFileName(exePath.ToString());
                        gameActive = (fileName == "BNSR.exe" ||
                                        exePath.ToString() == Process.GetCurrentProcess().MainModule.FileName);
                    }
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    // Ignore access denied errors. Those usually happen if the foreground window is running with
                    // admin permissions but we are not.
                    if (ex.ErrorCode == -2147467259)  // 0x80004005
                    {
                        gameActive = false;
                    }
                    else
                    {
                        logger.Log(LogLevel.Error, "XivWindowWatcher: {0}", ex.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Log(LogLevel.Error, "XivWindowWatcher: {0}", ex.ToString());
            }

            UpdateOverlays();
        }

        //wip - find a way to get inCombat status
        //
        //private void CombatStatusChanged(object sender, EventSources.CombatStatusChangedArgs e)
        //{
        //    inCombat = e.InCombat;
        //    UpdateOverlays();
        //}
    }
}
