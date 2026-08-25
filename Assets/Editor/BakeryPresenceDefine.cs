// BakeryPresenceDefine.cs
//
// Detects whether Bakery is installed and keeps the "BAKERY_INCLUDED" scripting define
// symbol in sync. LightmapTestHarness.cs and BakeryTempObjectSuppression.cs gate their
// Bakery-only code behind `#if BAKERY_INCLUDED`.
//
// This file must compile whether or not Bakery is present, so it never references a
// Bakery type directly (no `typeof(ftRenderLightmap)`). Detection instead does a
// string-name type lookup ("ftRenderLightmap") across all loaded assemblies.
//
// Uses the current (non-"...ForGroup") NamedBuildTarget-based scripting define API.
//
// SetScriptingDefineSymbols only triggers a recompile when the value actually changes,
// so this only calls it when the define's current state doesn't already match
// detection — otherwise every domain reload would trigger another recompile.
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

[InitializeOnLoad]
static class BakeryPresenceDefine
{
    const string Symbol = "BAKERY_INCLUDED";

    // Kept as a string only — see file header for why this can't be typeof(ftRenderLightmap).
    const string BakeryProbeTypeName = "ftRenderLightmap";

    static BakeryPresenceDefine()
    {
        Apply();
    }

    static void Apply()
    {
        bool bakeryPresent = DetectBakeryPresent();

        var namedTarget = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
        PlayerSettings.GetScriptingDefineSymbols(namedTarget, out var defines);

        bool hasSymbol = Array.IndexOf(defines, Symbol) >= 0;
        if (hasSymbol == bakeryPresent)
            return; // Already in the correct state — do nothing (avoids an unnecessary recompile).

        string[] newDefines = bakeryPresent
            ? defines.Append(Symbol).ToArray()
            : defines.Where(d => d != Symbol).ToArray();

        PlayerSettings.SetScriptingDefineSymbols(namedTarget, newDefines);

        Debug.Log($"[BakeryPresenceDefine] Bakery {(bakeryPresent ? "detected" : "not detected")} " +
            $"-> {Symbol} {(bakeryPresent ? "added to" : "removed from")} scripting define symbols " +
            $"for '{namedTarget.TargetName}'. Recompiling...");
    }

    static bool DetectBakeryPresent()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type found;
            try
            {
                found = asm.GetType(BakeryProbeTypeName);
            }
            catch
            {
                // A small number of assemblies (dynamic/reflection-emit, some dependency
                // shims) can throw on GetType lookups rather than returning null — skip
                // those rather than letting a single bad assembly abort detection for the
                // whole AppDomain.
                continue;
            }

            if (found != null)
                return true;
        }

        return false;
    }
}
