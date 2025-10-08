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
        int mult = state == GameState.None || state == GameState.Wait || state == GameState.Exevt || state == GameState.Menu || state == GameState.Pause ? 2 : 1;
        Application.targetFrameRate = state == GameState.Battle ? Settings.GetGameSpeedByIndex(Settings.instance.battleSpeed) :
                                      state == GameState.None || state == GameState.Wait || state == GameState.Exevt ? Settings.GetGameSpeedByIndex(Settings.instance.fieldSpeed) :
                                      state == GameState.Menu || state == GameState.Pause ? 30 : 30;

        if (Application.targetFrameRate != curFPS && Application.targetFrameRate > 30)
            RS2UI.speedupDisplay = 1000 * Application.targetFrameRate / 30 + Application.targetFrameRate * 2;
        else
            RS2UI.speedupDisplay = 0;

        Application.targetFrameRate *= mult;
        //RS2UI.doublefps = Application.targetFrameRate > 30;
        Sys.frametime = RS2UI.doublefps ? 16 : 33;
        Main.core.set_mans_speed();
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
        if (Input.GetKey(KeyCode.End))
            Application.targetFrameRate = 2000;
        else if(Application.targetFrameRate == 2000)
            TrackGameStateChanges.SetGameSpeedByState(TrackGameStateChanges.DetermineGameState(TrackGameStateChanges.newStateFlags));
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
        if (Core.man_speed_tbl[2] == 2 && RS2UI.doublefps)
        {
            for(int i = 0; i < Core.man_speed_tbl.Length; i += 4)
            {
                Core.man_speed_tbl[i + 1] *= 2;
                Core.man_speed_tbl[i + 2] /= 2;
                Core.man_speed_tbl[i + 3] /= 2;
            }
            Core.people_speed_tbl[3] = 0x55;
            for (int i = 4; i < Core.people_speed_tbl.Length; i += 4)
            {
                Core.people_speed_tbl[i + 2] *= 2;
                Core.people_speed_tbl[i] /= 2;
                Core.people_speed_tbl[i + 1] /= 2;
            }
        }
        else if(!RS2UI.doublefps && Core.man_speed_tbl[2] == 1)
        {
            for (int i = 0; i < Core.man_speed_tbl.Length; i += 4)
            {
                Core.man_speed_tbl[i + 1] /= 2;
                Core.man_speed_tbl[i + 2] *= 2;
                Core.man_speed_tbl[i + 3] *= 2;
            }
            Core.people_speed_tbl[3] = 0xFF;
            for (int i = 4; i < Core.people_speed_tbl.Length; i += 4)
            {
                Core.people_speed_tbl[i + 2] /= 2;
                Core.people_speed_tbl[i] *= 2;
                Core.people_speed_tbl[i + 1] *= 2;
            }
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
        GUI.Label(new Rect(100, 250, 200, 50), "Pixel perfect resolutions");
        if(GUI.Button(new Rect(100, 300, 100, 50), "960x540"))
            Screen.SetResolution(960, 540, false);
        if (GUI.Button(new Rect(100, 350, 100, 50), "1920x1080"))
            Screen.SetResolution(1920, 1080, false);
        if (GUI.Button(new Rect(100, 400, 100, 50), "2880x1620"))
            Screen.SetResolution(2880, 1620, false);
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

        if(__instance.speed_count == 4 && RS2UI.doublefps)
        {
            __instance.speed_count *= 2;
            __instance.speed_size_plus /= 2;
            __instance.speed_size_minus /= 2;
        }
    }
}

[HarmonyPatch(typeof(Core), "set_mans_speed")]
public static class FPSFixEmperor
{
    public static void Postfix(Core __instance)
    {
        if (RS2UI.doublefps)
        {
            __instance.now_speed_count *= 2;
            __instance.now_speed_size_plus /= 2;
            __instance.now_speed_size_minus /= 2;
        }
        else if(__instance.now_speed_count == 16 || (__instance.now_speed_count == 8 && __instance.vehicle_count != 0))
        {
            __instance.now_speed_count /= 2;
            __instance.now_speed_size_plus *= 2;
            __instance.now_speed_size_minus *= 2;
        }
    }
}

[HarmonyPatch(typeof(Core), "npc_obj_put")]
public static class FPSFixNPCPut
{
    public static void Prefix(Core __instance, ref int __state)
    {
        int num = 0;
        __instance.work_o[0] = 0;
        while (__instance.people[num].people_condition != 255)
        {
            __instance.work_o[0]++;
            num++;
        }
        __state = num;
    }
    public static void Postfix(Core __instance, ref int __state)
    {
        if (RS2UI.doublefps)
        {
            __instance.people[__state].people_add_plus = 1;
            __instance.people[__state].people_add_minus = -1;
            __instance.people[__state].people_speed_count = 16;
            __instance.people[__state].ori_people_speed_count = 16;
        }
    }
}

[HarmonyPatch(typeof(Core), "set_vehicle_people")]
public static class FPSFixVehicle
{
    public static void Postfix(Core __instance, int no, int p)
    {
        if (RS2UI.doublefps)
        {
            __instance.people[p].people_add_plus = 1;
            __instance.people[p].people_add_minus = -1;
            __instance.people[p].people_speed_count = 16;
            __instance.people[p].ori_people_speed_count = 16;
        }
    }
}

[HarmonyPatch(typeof(Core), "move_own_loop")]
public static class FPSFixEventMove
{
    public static bool Prefix(Core __instance)
    {
        if (!RS2UI.doublefps)
            return true;

        if (__instance.man_move_flag_h != 0)
        {
            __instance.main_x += __instance.man_move_add_h;
            __instance.man_move_flag_h -= 2;
            if (__instance.man_move_flag_h == 0)
            {
                if (__instance.man_move_flag_v == 0)
                {
                    __instance.man_anime++;
                    if (__instance.man_move_count_h == 0)
                    {
                        __instance.attribute_check_flag = 1;
                    }
                }
                if (__instance.man_move_count_h != 0)
                {
                    __instance.man_move_count_h--;
                    __instance.man_move_flag_h = __instance.speed_count;
                }
            }
        }
        if (__instance.man_move_flag_v != 0)
        {
            __instance.main_y += __instance.man_move_add_v;
            __instance.man_move_flag_v -= 2;
            if (__instance.man_move_flag_v == 0)
            {
                __instance.man_anime++;
                if (__instance.man_move_count_v == 0)
                {
                    __instance.attribute_check_flag = 1;
                }
                else
                {
                    __instance.man_move_count_v--;
                    __instance.man_move_flag_v = __instance.speed_count;
                }
            }
        }
        return false;
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
        if (x != 450)
            return;
        if ((y - 20) % 40 == 0)
            y = ((y - 20) / 40) * 30 + 20;
        x -= 147;
        __instance.SetPos(155+RS2UI.battleXOff+40, 106+RS2UI.battleYOff);
    }
}

[HarmonyPatch(typeof(GS), "DrawString")]
public static class ScrollText
{
    public static void Prefix(ref string str, ref int _x, ref int _y, int _z, Color32 color, GS.FontEffect effect, ref Vector2 __state)
    {
        __state = new Vector2(GS.m_font_scale_x, GS.m_font_scale_y);
        if (TrackGameStateChanges.DetermineGameState(TrackGameStateChanges.newStateFlags) != TrackGameStateChanges.GameState.Battle)
            return;
        Traverse.Create(Main.battle).Field("m_HelpScroll").SetValue(0);
        string m_HelpText = Traverse.Create(Main.battle).Field("m_HelpText").GetValue<string>();
        if (str == m_HelpText)
        {
            str = str.Replace("　　", "    ").Replace("Ally / ", "Ally    ");
            if (str.IndexOf("    ") < 0 && str.IndexOf('/') > 0)
                MelonLogger.Msg(str);
            _x = 175;
            _y = 470;
            GS.m_font_scale_x = 0.65f;
            GS.m_font_scale_y = 0.65f;
            int threshold = 80;

            int dotindex = str.Length > threshold ? str.LastIndexOf('.', threshold)+2 : -1;
            int bigspace = str.IndexOf("    ")+4;
            if (bigspace < 5 && str.Length < threshold)
                return;
            int add = bigspace > 4 && str.Length - bigspace < threshold ? 4 : dotindex > 10 && str.Length - dotindex < threshold ? 2 : 1;
            int index = add==4 ? bigspace : add==2 ? dotindex : str.LastIndexOf(' ', Mathf.Min(threshold, str.Length-1))+1;
            GS.DrawString(str.Substring(index), _x, _y+20, _z, color, effect);
            str = str.Substring(0, index);
            GS.m_font_scale_x = 0.65f;
            GS.m_font_scale_y = 0.65f;
        }
    }
    static void Postfix(ref Vector2 __state)
    {
        GS.m_font_scale_x = __state.x;
        GS.m_font_scale_y = __state.y;
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
public static class BattleWindowPosition
{
    static Dictionary<string, Rect> rect6 = new Dictionary<string, Rect>();
    public static void Postfix(string ObjectName, ref Rect __result, CSpriteStudioObject __instance)
    {
        if (TrackGameStateChanges.DetermineGameState(TrackGameStateChanges.newStateFlags) != TrackGameStateChanges.GameState.Battle)
            return;
        if (ObjectName.Contains("mtxt_mgc_n_") || ObjectName.Contains("mtxt_cmd_105_") || ObjectName.Contains("sttxt_jutsu_R_"))
        {
            int num = int.Parse(ObjectName.Substring(ObjectName.Length - 3));
            if (num == 6)
                rect6[ObjectName.Substring(0, 9)] = __result;
            else if (num > 6)
            {
                __result = new Rect(rect6[ObjectName.Substring(0, 9)]);
                __result.y += (num - 6) * 40;
            }
            __result.x += RS2UI.battleXOff + (ObjectName.Contains("sttxt_jutsu_R_") ? -107 : 0);
            __result.y += (num - 1) * -10 - 7 + RS2UI.battleYOff;
        }
        else if (ObjectName.Contains("mtxt_item_n"))
        {
            __result.x += RS2UI.battleXOff;
            __result.y += RS2UI.battleYOff+7;
        }
        else if (ObjectName.Contains("mtxt_player_n") || ObjectName.Contains("sttxt_jutsu"))
        {
            __result.x += 110;
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
        if (TrackGameStateChanges.DetermineGameState(TrackGameStateChanges.newStateFlags) != TrackGameStateChanges.GameState.Battle)
            return;
        for (int i = 0; i < ___m_AnalyzeList.Count; i++)
        {
            Vector3 add = Vector3.zero;
            if (___m_AnalyzeList[i].DrawString.Contains("Cost:"))
            {
                ___m_AnalyzeList[i].IsVisible = false;
                ___m_AnalyzeList[i].DrawRect.x = -2000;
                //___m_AnalyzeList[i].DrawString = ___m_AnalyzeList[i].DrawString.Replace("Cost:", "");
            }

            if (___m_AnalyzeList[i].SSTriangle2.name.Contains("btlwdw_1_b_2_a_3"))
            {
                add.x += RS2UI.battleXOff;
                add.y += -RS2UI.battleYOff-7;
            }
            else if (___m_AnalyzeList[i].SSTriangle2.name.Contains("btlwdw_1_b_2_a_1")
                || ___m_AnalyzeList[i].SSTriangle2.name.Contains("btlwdw_1_b_2_a_2")
                || ___m_AnalyzeList[i].SSTriangle2.name.Contains("slash_gr"))
            {
                add.x += 110;
            }

            if (add == Vector3.zero)
                continue;

            for (int j = 0; j < ___m_AnalyzeList[i].SSTriangle2.m_obj.m_anime[0].m_frames[0].m_meshes[___m_AnalyzeList[i].SSTriangle2.m_id - 1].m_vtx.Length; j++)
            {
                SSObject.Poly m = ___m_AnalyzeList[i].SSTriangle2.m_obj.m_anime[0].m_frames[0].m_meshes[___m_AnalyzeList[i].SSTriangle2.m_id - 1];
                ___m_AnalyzeList[i].SSTriangle2.m_obj.m_vtx_array[m.m_vtx[j]] += add + (add.y == 0 ? 0 : j < 2 ? -5 : 5) * Vector3.up
                                                                                     + (add.y == 0 ? 0 : j % 2 == 1 ? 50 : 0) * Vector3.left;
            }

            //if (___m_AnalyzeList[i].SSTriangle2.m_obj.m_pos == Vector3.zero && add != Vector3.zero)
            //{
            //    MelonLogger.Msg(___m_AnalyzeList[i].SSTriangle2.m_obj.m_tex[___m_AnalyzeList[i].SSTriangle2.m_id].m_name);
            //    for (int j = 0; j < ___m_AnalyzeList[i].SSTriangle2.m_obj.m_vtx_array.Length; j++)
            //    {
            //        if(___m_AnalyzeList[i].SSTriangle2.m_obj.m_tex[___m_AnalyzeList[i].SSTriangle2.m_id].m_name == ___m_AnalyzeList[i].SSTriangle2.name)
            //            ___m_AnalyzeList[i].SSTriangle2.m_obj.m_vtx_array[j] += add;
            //    }
            //    ___m_AnalyzeList[i].SSTriangle2.m_obj.SetPos(add);
            //}
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

//[HarmonyPatch]
//public static class HalfSpeedFunc
//{
//    public static IEnumerable<System.Reflection.MethodBase> TargetMethods()
//    {
//        yield return AccessTools.Method(typeof(Core), "message_lop");
//    }

//    public static bool Prefix()
//    {
//        if (RS2UI.doublefps && Time.frameCount % 2 == 0)
//            return false;
//        return true;
//    }
//}

[HarmonyPatch(typeof(Core), "message_lop")]
public static class TextSpacing
{
    static int flashDir = -1;

    static bool Prefix(Core __instance, int[] ___HumanName)
    {
        int i = (int)(__instance.mestbl[__instance.mess_type][Core.mess_adrs] & byte.MaxValue);
        if (__instance.mess_type == 1)
            i = ___HumanName[Core.mess_adrs] & 255;
        if (RS2UI.doublefps && Time.frameCount % 2 == 0 && Core.mess_adrs < __instance.end_mess_adrs)
        {
            if (i == 44)
                __instance.pause_sa();
            return false;
        }
        return true;
    }

    static void Postfix(ref Core __instance, int[] ___HumanName)
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

        int i2 = (int)(__instance.mestbl[__instance.mess_type][Core.mess_adrs] & byte.MaxValue);
        if (__instance.mess_type == 1)
            i2 = ___HumanName[Core.mess_adrs] & 255;

        //MelonLogger.Msg(Core.mess_adrs + " / " + __instance.end_mess_adrs);
        //float m_StringColorAdd = Traverse.Create(typeof(SpriteStudioCursor)).Field("m_StringColorAdd").GetValue<float>();
        //float m_StringColor = Traverse.Create(typeof(SpriteStudioCursor)).Field("m_StringColor").GetValue<float>();
        //Traverse.Create(typeof(SpriteStudioCursor)).Field("m_StringColor").SetValue(m_StringColor - m_StringColorAdd + m_StringColorAdd * Time.deltaTime * 15f);
    }
}

//[HarmonyPatch(typeof(Core), "message_lop2")]
//public static class EventScript
//{
//    static void Prefix(ref Core __instance, int[] ___HumanName)
//    {
//        int i = (int)(__instance.mestbl[__instance.mess_type][Core.mess_adrs] & byte.MaxValue);
//        if (__instance.mess_type == 1)
//        {
//            i = ___HumanName[Core.mess_adrs] & 255;
//        }
//        MelonLogger.Msg(i);
//    }
//}

[HarmonyPatch]
public static class DialogSpacingCursor
{
    public static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Window), "drawCursor");
        yield return AccessTools.Method(typeof(Window), "drawCursor2");
    }

    public static void Prefix(ref int y)
    {
        if (y % 16 == 0)
            y = y / 16 * 12;
    }
}

//[HarmonyPatch]
//public static class TextSpacing2
//{
//    public static IEnumerable<System.Reflection.MethodBase> TargetMethods()
//    {
//        yield return AccessTools.Method(typeof(Core), "tab_sa");
//        yield return AccessTools.Method(typeof(Core), "kaigyo_sub");
//        yield return AccessTools.Method(typeof(Core), "its2000kanji");
//        //yield return AccessTools.Method(typeof(Core), "message_lop2");
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
        public static int battleXOff = -135;
        public static int battleYOff = -42;
        public static int speedupDisplay = 0;
        public static bool doublefps = true;
        public override void OnApplicationQuit()
        {
            base.OnApplicationQuit();
            Settings.WriteSettings();
        }
    }
}