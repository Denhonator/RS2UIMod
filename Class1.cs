using System;
using UnityEngine;
using HarmonyLib;
using SEAD;
using System.IO;
using MelonLoader;
using RS2;
using UnityEngine.Rendering;
using System.Collections;
using System.Collections.Generic;
using System.Reflection.Emit;

[Serializable]
public class Settings
{
    static Settings()
    {
        LoadSettings();
    }

    public static string FilePath = Application.persistentDataPath + "/__modsettings.json";

    public static void LoadSettings()
    {
        if (File.Exists(FilePath))
        {
            instance = JsonUtility.FromJson<Settings>(File.ReadAllText(FilePath));
            return;
        }

        instance = new Settings();
        WriteSettings();
    }
    public static void WriteSettings()
    {
        File.WriteAllText(FilePath, JsonUtility.ToJson(instance, true));
    }

    private static int sanitizeSpeed(int fps) { return fps < 1 ? 30 : fps; }

    public static int GetGameSpeedByIndex(int idx)
    {
        return idx == 0 ? instance.normalFps :
                idx == 1 ? instance.fastFps :
                idx == 2 ? instance.turboFps :
                            instance.normalFps;
    }

    public static Settings instance = new Settings();

    public bool skipLogos = true;

    public int normalFps = 30;
    public int fastFps = 60;
    public int turboFps = 90;

    public int battleSpeed = 0;
    public int fieldSpeed = 0;

    public bool dungeonExit = true;
    public float bgmVolume = 0.1f;
    public float seVolume = 1f;
}

[HarmonyPatch(typeof(Menu), "_logo")]
public static class SkipLogos
{
    public static bool Prefix(ref int __result)
    {
        if (Settings.instance.skipLogos)
        {
            __result = 0;
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(Menu), "load_a2")]
public static class SetSpeedOnLoadFile
{
    public static void Postfix(Menu __instance)
    {
        if (__instance.load_save == 99)
        {
            TrackGameStateChanges.SetGameSpeedByState(TrackGameStateChanges.GameState.None);
        }
    }
}

[HarmonyPatch(typeof(Core), "reset")]
public static class QuickReset
{
    public static void Postfix()
    {
        TrackGameStateChanges.IgnoreNextStateChange = true;
        TrackGameStateChanges.SetGameSpeedByState(TrackGameStateChanges.GameState.Menu);
    }
}

[HarmonyPatch(typeof(Core), "sync")]
public static class TrackGameStateChanges
{
    public enum GameState
    {
        None,
        Exdemo,
        Exevt,
        Battle,
        Menu,
        Mapchg,
        Pause,
        Wait,
    }
    public class StateFlags
    {
        public int exdemo_flg;
        public int exevt_flg;
        public int battle_flg;
        public int menu_flg;
        public int mapchg_flg;
        public int pause_flg;
        public int wait_flg;
    }
    public static StateFlags oldStateFlags = new StateFlags()
    {
        exdemo_flg = 0,
        exevt_flg = 0,
        battle_flg = 0,
        menu_flg = 0,
        mapchg_flg = 0,
        pause_flg = 0,
        wait_flg = 0
    };
    public static StateFlags newStateFlags = new StateFlags()
    {
        exdemo_flg = 0,
        exevt_flg = 0,
        battle_flg = 0,
        menu_flg = 0,
        mapchg_flg = 0,
        pause_flg = 0,
        wait_flg = 0
    };

    public static bool IgnoreNextStateChange = false;

    public static GameState DetermineGameState(StateFlags sf)
    {
        return sf.exdemo_flg != 0 ? GameState.Exdemo :
            sf.exevt_flg != 0 ? GameState.Exevt :
        sf.battle_flg != 0 ? GameState.Battle :
            sf.menu_flg != 0 ? GameState.Menu :
        sf.mapchg_flg != 0 ? GameState.Mapchg :
            sf.pause_flg != 0 ? GameState.Pause :
            sf.wait_flg != 0 ? GameState.Wait :
                                GameState.None;
    }

    public static void SetGameSpeedByState(GameState state)
    {
        int curFPS = Application.targetFrameRate;
        Application.targetFrameRate = state == GameState.Battle ? Settings.GetGameSpeedByIndex(Settings.instance.battleSpeed) :
                                      state == GameState.None || state == GameState.Wait || state == GameState.Exevt ? Settings.GetGameSpeedByIndex(Settings.instance.fieldSpeed) :
                                      state == GameState.Menu || state == GameState.Pause ? 30 : 30;

        if (Application.targetFrameRate != curFPS && Application.targetFrameRate > 30)
        {
            RS2UI.speedupDisplay = 1000 * Application.targetFrameRate / 30 + Application.targetFrameRate * 2;
        }
        else
        {
            RS2UI.speedupDisplay = 0;
        }
    }

    public static void CopyStateFlagsTo(Core x, StateFlags sf)
    {
        sf.battle_flg = x.battle_flg;
        sf.exdemo_flg = x.exdemo_flg;
        sf.exevt_flg = x.exevt_flg;
        sf.mapchg_flg = x.mapchgflg;
        sf.menu_flg = x.tono_flg;
        sf.pause_flg = x.pauseflg;
        sf.wait_flg = x.waitflg;
    }

    public static void IncrementCurrentGameStateSpeed()
    {
        GameState s = DetermineGameState(newStateFlags);
        if (s == GameState.Battle)
        {
            Settings.instance.battleSpeed = (Settings.instance.battleSpeed + 1) % 3;
            SetGameSpeedByState(s);
        }
        else if (s == GameState.None || s == GameState.Wait || s == GameState.Exevt)
        {
            Settings.instance.fieldSpeed = (Settings.instance.fieldSpeed + 1) % 3;
            SetGameSpeedByState(s);
        }
    }

    public static void Prefix(Core __instance)
    {
        CopyStateFlagsTo(__instance, oldStateFlags);
    }

    public static void Postfix(Core __instance)
    {
        CopyStateFlagsTo(__instance, newStateFlags);

        GameState oldState = DetermineGameState(oldStateFlags);
        GameState newState = DetermineGameState(newStateFlags);

        if (oldState != newState && !IgnoreNextStateChange)
        {
            //System.IO.File.AppendAllText("test.txt", $"Detected state change {{{oldState} => {newState}}}\n");
            SetGameSpeedByState(newState);
        }

        IgnoreNextStateChange = false;
    }
}

[HarmonyPatch(typeof(GameMain), "Update")]
public static class SpeedOptions
{
    static GameObject dbg;
    public static void Prefix()
    {
        if (Input.GetKeyDown(KeyCode.PageDown) || Input.GetKeyDown(KeyCode.JoystickButton9))
        {
            TrackGameStateChanges.IncrementCurrentGameStateSpeed();
        }
        if (Input.GetKeyDown(KeyCode.Home))
        {
            if (dbg == null)
            {
                dbg = new GameObject("dbg");
                dbg.AddComponent<Simple>();
            }
            else
                dbg.GetComponent<Simple>().enabled = !dbg.GetComponent<Simple>().enabled;
        }
        if (RS2UI.speedupDisplay % 1000 > 0)
        {
            GS.DrawString((RS2UI.speedupDisplay / 1000) + "x", 4, 0, 0, Color.white, GS.FontEffect.SHADOW);
            RS2UI.speedupDisplay--;
        }
    }
}

[HarmonyPatch]
public static class DebugMenuAwake
{
    public static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Simple), "Awake");
        yield return AccessTools.Method(typeof(Simple), "Update");
        yield return AccessTools.Method(typeof(Simple), "Draw");
    }

    public static bool Prefix(ref int ___m_menu_mode)
    {
        ___m_menu_mode = 7;
        return false;
    }
}

[HarmonyPatch(typeof(Simple), "Start")]
public static class DebugMenuStart
{
    public static bool Prefix(Simple __instance)
    {
        System.Reflection.MethodInfo dynMethod = __instance.GetType().GetMethod("InitDebugMenu",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        dynMethod.Invoke(__instance, new object[] { });
        return false;
    }
}

[HarmonyPatch(typeof(DebugMenu), "DrawDebugStrings")]
public static class DebugText
{
    public static string print = "";
    public static void Prefix()
    {
        DebugMenu.print(300, 50, print);
        DebugMenu.print(100, 50, "SFX Volume");
        DebugMenu.print(100, 150, "BGM Volume");
        Settings.instance.seVolume = GUI.HorizontalSlider(new Rect(100, 100, 200, 100), Settings.instance.seVolume, 0, 1);
        Settings.instance.bgmVolume = GUI.HorizontalSlider(new Rect(100, 200, 200, 100), Settings.instance.bgmVolume, 0, 1);
    }
}

[HarmonyPatch(typeof(Core), "change_speed")]
public static class MapAnywhere
{
    public static float keyHold = 0;
    public static void Postfix(Core __instance)
    {
        if (Settings.instance.dungeonExit && keyHold > 0.6f)
            __instance.exit_ivent = Main.map.maptbl.exitevent;

        if ((Util.GetKeypadState() & 32) != 0)
        {
            keyHold += Time.deltaTime;
        }
        else
            keyHold = 0;
        DebugText.print = Util.GetKeypadState().ToString();
    }
}

[HarmonyPatch(typeof(Util), "falloc", new Type[] { typeof(string) })]
public static class ReplaceTexture
{
    public static bool Prefix(ref string fname, ref byte[] __result)
    {
        try
        {
            if (File.Exists("ReplaceFile/" + fname))
            {
                try
                {
                    __result = File.ReadAllBytes("ReplaceFile/" + fname);
                    return false;
                    //___m_regist_texture[Path.GetFileNameWithoutExtension(fname)] = __result;
                }
                catch
                {
                    MelonLogger.Msg("Failed to replace" + fname);
                }
            }
            else
            {
                if (!Directory.Exists(Path.GetDirectoryName("Extract/" + fname)))
                    Directory.CreateDirectory(Path.GetDirectoryName("Extract/" + fname));
                if (!File.Exists("Extract/" + fname))
                    File.WriteAllBytes("Extract/" + fname, Util.m_arc_file.ReadFile(fname));
                return true;
            }
        }
        catch
        {
            MelonLogger.Msg("Failed to save " + fname);
        }
        return true;
    }
}

[HarmonyPatch(typeof(CSound), "SetVolume")]
public static class AudioVolume
{
    public static void Prefix(CSound __instance, ref float Volume)
    {
        if(Volume > 0f)
            Volume = __instance.GetFileName().Contains("BGM") ? Settings.instance.bgmVolume : Settings.instance.seVolume;
    }
}

[HarmonyPatch(typeof(CSound), "Load")]
public static class AudioInit
{
    public static void Prefix(ref string ___m_FileName, ref bool __state)
    {
        __state = ___m_FileName.Contains("BGM");
    }
    public static void Postfix(ref ulong ___m_SoundID, ref ulong ___m_BankID, bool __state)
    {
        if (API.seadSoundGetState(___m_SoundID) != SEAD_SOUND_STATE.INVALID)
        {
            return;
        }
        int number = 0;
        float fadeInTime = 0f;
        float seekTime = 0f;
        API.seadBankCreateSound(___m_BankID, ref ___m_SoundID, number, SEAD_SOUND_PORT.AUTO);
        if (__state)
            API.seadSoundSetVolume(___m_SoundID, Settings.instance.bgmVolume, 0);
        else
            API.seadSoundSetVolume(___m_SoundID, Settings.instance.seVolume, 0);
        API.seadSoundPlay(___m_SoundID, fadeInTime, seekTime);
    }
}

[HarmonyPatch(typeof(SoundManager), "Update")]
public static class AudioMixer
{
    public static void Prefix()
    {
        List<CSound> bgm = Traverse.Create(SoundManager.m_SoundList_BGM).Field("m_SoundList").GetValue<List<CSound>>();
        List<CSound> se = Traverse.Create(SoundManager.m_SoundList_SE).Field("m_SoundList").GetValue<List<CSound>>();
        for (int i = 0; i < 10; i++)
        {
            if (se.Count > i && SoundManager.m_SoundList_SE.GetSound(i).GetMode() == CSound.MODE.INITIALIZE)
            {
                SoundManager.m_SoundList_SE.GetSound(i).Load();
                SoundManager.m_SoundList_SE.GetSound(i).SetMode(CSound.MODE.PLAY);
            }
            if (bgm.Count > i && SoundManager.m_SoundList_BGM.GetSound(i).GetMode() == CSound.MODE.INITIALIZE)
            {
                SoundManager.m_SoundList_BGM.GetSound(i).Load();
                SoundManager.m_SoundList_BGM.GetSound(i).SetMode(CSound.MODE.PLAY);
            }
        }
    }
    public static void Postfix()
    {
        List<CSound> bgm = Traverse.Create(SoundManager.m_SoundList_BGM).Field("m_SoundList").GetValue<List<CSound>>();
        List<CSound> se = Traverse.Create(SoundManager.m_SoundList_SE).Field("m_SoundList").GetValue<List<CSound>>();
        for (int i = 0; i < 10; i++)
        {
            if(bgm.Count > i)
                SoundManager.m_SoundList_BGM.GetSound(i).SetVolume(Settings.instance.bgmVolume);
            if(se.Count > i)
                SoundManager.m_SoundList_SE.GetSound(i).SetVolume(Settings.instance.seVolume);
        }
    }
}

[HarmonyPatch(typeof(GS), "InitFont")]
public static class FontChange
{
    public static bool Prefix(GS.FontType type, string name, int priority)
    {
        if (GS.m_font[(int)type] != null)
        {
            Resources.UnloadAsset(GS.m_font[(int)type]);
            Util.Destroy(GS.m_font_mtl[(int)type]);
            Util.Destroy(GS.m_shadow_mtl[(int)type]);
        }
        if (File.Exists("rs3font"))
        {
            AssetBundle ab = AssetBundle.LoadFromFile("rs3font");
            foreach (string s in ab.GetAllAssetNames())
                MelonLogger.Msg(s);
            GS.m_font[(int)type] = ab.LoadAsset<Font>("rs3font.ttf");
            MelonLogger.Msg("Loaded rs3font.ttf");
            ab.Unload(false);
        }
        else
            GS.m_font[(int)type] = (Font)Resources.Load(name);

        GS.m_font_mtl[(int)type] = ShaderUtil.CreateMaterial(ShaderUtil.Type.FONT);
        GS.m_font_mtl[(int)type].mainTexture = GS.m_font[(int)type].material.mainTexture;
        GS.m_font_mtl[(int)type].color = new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
        GS.m_font_mtl[(int)type].renderQueue = priority + 1;
        ShaderUtil.SetDepthTest(GS.m_font_mtl[(int)type], CompareFunction.Always);
        GS.m_shadow_mtl[(int)type] = new Material(GS.m_font_mtl[(int)type]);
        ShaderUtil.SetDepthTest(GS.m_shadow_mtl[(int)type], CompareFunction.Always);
        GS.m_shadow_mtl[(int)type].renderQueue = priority;
        GS.m_shadow_mtl[(int)type].color = new Color32(0, 0, 0, 128);
        GS.m_font_mtl_w[(int)type] = new Material(GS.m_font_mtl[(int)type]);
        ShaderUtil.SetDepthTest(GS.m_font_mtl_w[(int)type], CompareFunction.Equal);
        ShaderUtil.SetDepth(GS.m_font_mtl_w[(int)type], 0.5f);
        GS.m_font_mtl_w[(int)type].renderQueue = priority + 1;
        GS.m_shadow_mtl_w[(int)type] = new Material(GS.m_font_mtl_w[(int)type]);
        ShaderUtil.SetDepthTest(GS.m_shadow_mtl_w[(int)type], CompareFunction.Equal);
        ShaderUtil.SetDepth(GS.m_shadow_mtl_w[(int)type], 0.5f);
        GS.m_shadow_mtl_w[(int)type].renderQueue = priority;
        GS.m_shadow_mtl_w[(int)type].color = new Color32(0, 0, 0, 128);
        return false;
    }
}

[HarmonyPatch(typeof(Window), "draw")]
public static class FontColor
{
    public static void Prefix(ref Color32 ___m_msg_color0)
    {
        ___m_msg_color0 = Color.white;
    }
}

[HarmonyPatch(typeof(Window), "set")]
public static class TextboxDimensions
{
    public static void Prefix(int x, int y, int w, ref int h, int mw, ref int mh, int mode, ref Window __instance)
    {
        h = h * 3 / 4;
        mh = mh * 3 / 4;
    }
}

[HarmonyPatch(typeof(Window), "rollUp")]
public static class TextboxDimensions2
{
    public static void Prefix(ref int l)
    {
        if (l == 8)
            l = 6;
    }
}

[HarmonyPatch(typeof(CBattleWindow), "SetSize")]
public static class BattleWindowSize
{
    public static void Prefix(ref CBattleWindow __instance, ref int x, ref int y)
    {
        if ((y - 20) % 40 == 0)
            y = ((y - 20) / 40) * 30 + 20;
        x -= 80;
        __instance.SetPos(155+RS2UI.battleXOff, 106+RS2UI.battleYOff);
    }
}

[HarmonyPatch(typeof(Battle), "sync")]
public static class CommandListMax
{
    public static void Prefix(ref string[] ___ObjectNameCommand, ref string[] ___ObjectNameUse, ref string[] ___ObjectNamePoint)
    {
        if (___ObjectNameCommand.Length < 12) {
            Traverse.Create(typeof(Battle)).Field("MenuMax").SetValue(12);
            ___ObjectNameCommand = new string[12];
            ___ObjectNameUse = new string[12];
            ___ObjectNamePoint = new string[12];
            for (int i = 1; i <= 12; i++){
                ___ObjectNameCommand[i-1] = "mtxt_mgc_n_" + i.ToString("D3");
                ___ObjectNameUse[i-1] = "mtxt_cmd_105_" + i.ToString("D3");
                ___ObjectNamePoint[i-1] = "sttxt_jutsu_R_" + i.ToString("D3");
            }
        }
    }
}

[HarmonyPatch(typeof(Battle), "SetVisibleBattleUI")]
public static class CommandListMaxTouch
{
    public static void Postfix(bool IsVisible, ref CSpriteStudioObject ___UiBattle)
    {
        for(int i = 7; i <= 12; i++)
        {
            InputManager.set_enable_touch("mtxt_mgc_n_"+i.ToString("D3"), IsVisible);
        }
        InputManager.set_enable_touch("btn_joho", false);
        ___UiBattle.SetVisible("btn_joho", false);
        InputManager.set_enable_touch("L_btn", false);
        ___UiBattle.SetVisible("L_btn", false);
        InputManager.set_enable_touch("R_btn", false);
        ___UiBattle.SetVisible("R_btn", false);
    }
}

[HarmonyPatch(typeof(Battle), "InitializeBattleUI")]
public static class CommandListMaxTouch2
{
    public static void Postfix(ref CSpriteStudioObject ___UiBattle)
    {
        for (int i = 7; i <= 12; i++)
        {
            ___UiBattle.AddTouchRect("mtxt_mgc_n_" + i.ToString("D3"), 1f, false);
        }
    }
}

[HarmonyPatch(typeof(CSpriteStudioObject), "GetSpriteRect")]
public static class CommandListMax3
{
    static Dictionary<string, Rect> rect6 = new Dictionary<string, Rect>();
    public static void Postfix(string ObjectName, ref Rect __result, CSpriteStudioObject __instance)
    {
        if (ObjectName.Contains("mtxt_mgc_n_") || ObjectName.Contains("mtxt_cmd_105_") || ObjectName.Contains("sttxt_jutsu_R_"))
        {
            int num = int.Parse(ObjectName.Substring(ObjectName.Length - 3));
            if (num == 6)
                rect6[ObjectName.Substring(0,9)] = __result;
            else if (num > 6)
            {
                __result = new Rect(rect6[ObjectName.Substring(0, 9)]);
                __result.y += (num - 6) * 40;
            } 
            __result.x += RS2UI.battleXOff + (ObjectName.Contains("sttxt_jutsu_R_") ? -80 : 0);
            __result.y += (num - 1) * -10 - 7 + RS2UI.battleYOff;
        }
    }
}

[HarmonyPatch(typeof(CSpriteStudioObject), "AnalyzeObjectName")]
public static class CommandListMax2
{
    public static void Prefix(ref SSObject ___m_Root)
    {
        for(int i = 0; i < ___m_Root.m_parts_list.Count; i++)
        {
            if (___m_Root.m_parts_list[i].name.Contains("mtxt_mgc_n_") || ___m_Root.m_parts_list[i].name.Contains("mtxt_cmd_105_") || ___m_Root.m_parts_list[i].name.Contains("sttxt_jutsu_R_"))
            {
                if (int.Parse(___m_Root.m_parts_list[i].name.Substring(___m_Root.m_parts_list[i].name.Length - 3)) == 6)
                {
                    for (int j = 7; j <= 12; j++)
                    {
                        SSObject.Parts newpart = new SSObject.Parts();
                        newpart.name = ___m_Root.m_parts_list[i].name.Replace("006", j.ToString("D3"));
                        newpart.m_obj = ___m_Root.m_parts_list[i].m_obj;
                        newpart.m_id = 33;
                        ___m_Root.m_parts_list.Add(newpart);
                    }
                }
            }
        }
    }
    public static void Postfix(ref List<CSpriteAnalyze> ___m_AnalyzeList)
    {
        Rect rect6 = Rect.zero;
        for (int i = 0; i < ___m_AnalyzeList.Count; i++)
        {
            ___m_AnalyzeList[i].DrawString = ___m_AnalyzeList[i].DrawString.Replace("Cost:", "");
        }
    }
}

//[HarmonyPatch(typeof(SpriteStudioCursor), "SetVisibleFreeCursor")]
//public static class TextboxCursor
//{
//    public static void Prefix(ref int y)
//    {
//        MelonLogger.Msg(y);
//    }
//}

[HarmonyPatch]
public static class DialogSpacing
{
    public static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Window), "drawString");
        yield return AccessTools.Method(typeof(Window), "drawString2");
        yield return AccessTools.Method(typeof(Window), "drawString8");
    }

    public static void Prefix(ref int cy)
    {
        if(cy % 16 == 0)
            cy = (cy / 16) * 12;
    }
}

[HarmonyPatch(typeof(Core), "message_lop")]
public static class TextSpacing
{
    static int flashDir = -1;
    static void Postfix(ref Core __instance)
    {
        for(int i = 0; i < __instance.select_adrs.Length; i++)
        {
            if (__instance.select_adrs[i] % 16 == 0 && __instance.select_adrs[i] > 0)
                __instance.select_adrs[i] = (__instance.select_adrs[i] / 16) * 12;
        }
        if (__instance.cursor_cnt >= 255)
            flashDir = -1;
        else if (__instance.cursor_cnt <= 32)
            flashDir = 1;
        __instance.cursor_cnt = Mathf.Clamp(__instance.cursor_cnt - 32 + (int)(flashDir * 32 * 15 * Time.deltaTime), 0, 256);
        //float m_StringColorAdd = Traverse.Create(typeof(SpriteStudioCursor)).Field("m_StringColorAdd").GetValue<float>();
        //float m_StringColor = Traverse.Create(typeof(SpriteStudioCursor)).Field("m_StringColor").GetValue<float>();
        //Traverse.Create(typeof(SpriteStudioCursor)).Field("m_StringColor").SetValue(m_StringColor - m_StringColorAdd + m_StringColorAdd * Time.deltaTime * 15f);
    }
}

//[HarmonyPatch]
//public static class DialogSpacingCursor
//{
//    public static IEnumerable<System.Reflection.MethodBase> TargetMethods()
//    {
//        yield return AccessTools.Method(typeof(Window), "drawCursor");
//        yield return AccessTools.Method(typeof(Window), "drawCursor2");
//    }

//    public static void Prefix(ref int y)
//    {
//        y -= 16;
//    }
//}

//[HarmonyPatch]
//public static class TextSpacing
//{
//    public static IEnumerable<System.Reflection.MethodBase> TargetMethods()
//    {
//        yield return AccessTools.Method(typeof(Core), "tab_sa");
//        yield return AccessTools.Method(typeof(Core), "kaigyo_sub");
//    }

//    public static void Prefix(ref Core __instance, ref int __state)
//    {
//        __state = __instance.cursor_y[__instance.wind_which];
//    }

//    public static void Postfix(ref Core __instance, ref int __state)
//    {
//        if (__instance.cursor_y[__instance.wind_which] > __state)
//            __instance.cursor_y[__instance.wind_which] -= 4;
//    }
//}

namespace RS2
{
    public class RS2UI : MelonMod
    {
        public static int battleXOff = -125;
        public static int battleYOff = 0;
        public static int speedupDisplay = 0;
        public override void OnApplicationQuit()
        {
            base.OnApplicationQuit();
            Settings.WriteSettings();
        }
    }
}