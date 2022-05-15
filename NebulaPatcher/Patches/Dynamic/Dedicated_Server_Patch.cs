﻿using HarmonyLib;
using NebulaModel;
using NebulaModel.Logger;
using NebulaWorld;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using UnityEngine;

namespace NebulaPatcher.Patches.Dynamic
{
    // Collections of patches that need to make game run in nographics mode
    // This part only get patch when Multiplayer.IsDedicated is true
    internal class Dedicated_Server_Patch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameMain), nameof(GameMain.Begin))]
        public static void GameMainBegin_Postfix()
        {
            if (Multiplayer.IsActive && Config.Options.AutoPauseEnabled)
            {
                Log.Info("AutoPauseEnabled");
                GameMain.Pause();
            }
        }

        // Stop game rendering
        [HarmonyPrefix]
        [HarmonyPatch(typeof(GameData), nameof(GameData.OnDraw))]
        [HarmonyPatch(typeof(GameData), nameof(GameData.OnPostDraw))]
        [HarmonyPatch(typeof(GameMain), nameof(GameMain.LateUpdate))]
        [HarmonyPatch(typeof(FactoryModel), nameof(FactoryModel.LateUpdate))]
        public static bool OnDraw_Prefix()
        {
            return false;
        }

        // Destory gameObject so Update() won't execute
        [HarmonyPrefix]
        [HarmonyPatch(typeof(PlanetAtmoBlur), nameof(PlanetAtmoBlur.Start))]
        public static bool PlanetAtmoBlur_Start(PlanetAtmoBlur __instance)
        {
            UnityEngine.Object.Destroy(__instance.gameObject);
            return false;
        }

        // RenderTexture is not support, so disable all functions using the constructor
        [HarmonyPrefix]
        [HarmonyPatch(typeof(DysonMapCamera), nameof(DysonMapCamera.CheckOrCreateRTex))]
        [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.CaptureScreenShot), new Type[] { typeof(int), typeof(int) })] // Save won't have a img preview when disable
        [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.CaptureScreenShot), new Type[] { typeof(int), typeof(int), typeof(string) })]
        [HarmonyPatch(typeof(MechaEditorCamera), nameof(MechaEditorCamera.CheckOrCreateRTex))]
        [HarmonyPatch(typeof(UIDysonOrbitPreview), nameof(UIDysonOrbitPreview.CheckOrCreateRTex))]
        [HarmonyPatch(typeof(UIMechaMaterialBall), nameof(UIMechaMaterialBall._OnCreate))]
        [HarmonyPatch(typeof(UIMechaSaveGroup), nameof(UIMechaSaveGroup._OnCreate))]
        [HarmonyPatch(typeof(UIMilkyWay), nameof(UIMilkyWay.CheckOrCreateRTex))]
        [HarmonyPatch(typeof(UIMinimap3DControl), nameof(UIMinimap3DControl._OnCreate))]
        [HarmonyPatch(typeof(UISplitterWindow), nameof(UISplitterWindow._OnCreate))]
        [HarmonyPatch(typeof(UIStarmap), nameof(UIMilkyWay.CheckOrCreateRTex))]
        [HarmonyPatch(typeof(TranslucentImageSource), nameof(TranslucentImageSource.CreateNewBlurredScreen))]
        static bool RenderTexture_Prefix()
        {
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ComputeShader), nameof(ComputeShader.FindKernel))]
        [HarmonyPatch(typeof(ComputeShader), nameof(ComputeShader.GetKernelThreadGroupSizes))]
        public static bool ComputeShader_Prefix()
        {
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ComputeBuffer), nameof(ComputeBuffer.SetData), new Type[] { typeof(Array) })]
        [HarmonyPatch(typeof(ComputeBuffer), nameof(ComputeBuffer.GetData), new Type[] { typeof(Array), typeof(int), typeof(int), typeof(int) })] //DysonSwarm.Export
        public static bool ComputeBuffer_Prefix()
        {
            return false;
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(DysonSwarm), nameof(DysonSwarm.GameTick))]
        private static IEnumerable<CodeInstruction> DysonSwarmGameTick_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // Remove first part of the functions about computeShader
            // (this.computeShader.Set..., this.Dispatch_UpdateVel, this.Dispatch_UpdatePos)
            try
            {
                CodeMatcher matcher = new CodeMatcher(instructions)
                    .MatchForward(false, new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(DysonSwarm), nameof(DysonSwarm.Dispatch_UpdatePos))));
                int num = matcher.Pos + 1;
                matcher.Start()
                    .RemoveInstructions(num);
                return matcher.InstructionEnumeration();
            }
            catch
            {
                Log.Error("DysonSwarmGameTick_Transpiler failed. Mod version not compatible with game version.");
                return instructions;
            }
        }
    }
}