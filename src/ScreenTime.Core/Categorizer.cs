using System;
using System.Collections.Generic;
using System.IO;

namespace ScreenTime.Core;

/// <summary>应用分类：内置规则，后续可在设置中扩展。</summary>
public static class Categorizer
{
    public static readonly string[] Categories =
        { "社交", "娱乐", "游戏", "办公", "开发", "浏览器", "系统", "其他" };

    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // 浏览器
        ["chrome"] = "浏览器", ["msedge"] = "浏览器", ["firefox"] = "浏览器", ["brave"] = "浏览器",
        ["opera"] = "浏览器", ["opera_gx"] = "浏览器", ["vivaldi"] = "浏览器", ["360se"] = "浏览器",
        ["360chrome"] = "浏览器", ["qqbrowser"] = "浏览器", ["sogou_explorer"] = "浏览器", ["maxthon"] = "浏览器",

        // 社交
        ["wechat"] = "社交", ["weixin"] = "社交", ["qq"] = "社交", ["wxwork"] = "社交",
        ["dingtalk"] = "社交", ["telegram"] = "社交", ["feishu"] = "社交", ["lark"] = "社交",
        ["slack"] = "社交", ["discord"] = "社交", ["tim"] = "社交", ["qqpcmgr"] = "社交",

        // 游戏
        ["steam"] = "游戏", ["steamwebhelper"] = "游戏", ["epicgameslauncher"] = "游戏",
        ["riotclientservices"] = "游戏", ["leagueclient"] = "游戏", ["leagueclientux"] = "游戏",
        ["cs2"] = "游戏", ["javaw"] = "游戏", ["x360ce"] = "游戏", ["battlenet"] = "游戏",
        ["wegame"] = "游戏", ["mihoyo launcher"] = "游戏", ["yuanshen"] = "游戏", ["zenlesszonezero"] = "游戏",

        // 办公
        ["winword"] = "办公", ["excel"] = "办公", ["powerpnt"] = "办公", ["outlook"] = "办公",
        ["onenote"] = "办公", ["wps"] = "办公", ["wpspdf"] = "办公", ["et"] = "办公", ["wpp"] = "办公",
        ["acrobat"] = "办公", ["acrord32"] = "办公", ["sumatrapdf"] = "办公", ["foxitreader"] = "办公",
        ["notepad"] = "办公", ["notepad++"] = "办公", ["notepad3"] = "办公", ["calibre"] = "办公",
        ["typora"] = "办公", ["obsidian"] = "办公", ["onenotem"] = "办公", ["snipaste"] = "办公",

        // 开发
        ["code"] = "开发", ["devenv"] = "开发", ["clion64"] = "开发", ["pycharm64"] = "开发",
        ["goland64"] = "开发", ["webstorm64"] = "开发", ["rider64"] = "开发", ["idea64"] = "开发",
        ["datagrip64"] = "开发", ["goland"] = "开发", ["windowsterminal"] = "开发", ["conhost"] = "开发",
        ["powershell"] = "开发", ["pwsh"] = "开发", ["cmd"] = "开发", ["wt"] = "开发",
        ["git-bash"] = "开发", ["githubdesktop"] = "开发", ["fork"] = "开发", ["sourcetree"] = "开发",
        ["postman"] = "开发", ["docker"] = "开发", ["dotnet"] = "开发", ["heidisql"] = "开发",
        ["navicat"] = "开发", ["xshell"] = "开发", ["putty"] = "开发", ["winscp"] = "开发",

        // 娱乐
        ["cloudmusic"] = "娱乐", ["qqmusic"] = "娱乐", ["kugou"] = "娱乐", ["kuwo"] = "娱乐",
        ["spotify"] = "娱乐", ["bilibili"] = "娱乐", ["potplayer"] = "娱乐", ["vlc"] = "娱乐",
        ["mpv"] = "娱乐", ["iqiyi"] = "娱乐", ["youku"] = "娱乐", ["mpc-hc64"] = "娱乐",
        ["douban"] = "娱乐", ["steam?"] = "娱乐",

        // 系统
        ["explorer"] = "系统", ["taskmgr"] = "系统", ["control"] = "系统", ["systemsettings"] = "系统",
        ["searchhost"] = "系统", ["startmenuexperiencehost"] = "系统", ["textinputhost"] = "系统",
        ["runtimebroker"] = "系统", ["applicationframehost"] = "系统", ["shellexperiencehost"] = "系统",
        ["mspaint"] = "系统", ["snippingtool"] = "系统", ["screenclippinghost"] = "系统",
        ["magnify"] = "系统", ["osk"] = "系统", ["cleanmgr"] = "系统", ["dfrgui"] = "系统",
        ["recoverydrive"] = "系统", ["winver"] = "系统", ["setings"] = "系统",
    };

    private static volatile Dictionary<string, string> _overrides = new(StringComparer.Ordinal);

    /// <summary>用户自定义分类覆盖（key 为应用完整路径小写）。</summary>
    public static void SetOverrides(Dictionary<string, string> overrides) =>
        _overrides = new Dictionary<string, string>(overrides, StringComparer.Ordinal);

    public static void SetOverride(string appKey, string? category)
    {
        var dict = new Dictionary<string, string>(_overrides, StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(category)) dict.Remove(appKey);
        else dict[appKey] = category;
        _overrides = dict;
    }

    public static string? OverrideOf(string appKey) =>
        _overrides.TryGetValue(appKey, out var c) ? c : null;

    /// <summary>appKey 为 exe 完整路径（小写）。用户自定义分类优先于内置规则。</summary>
    public static string Of(string appKey)
    {
        if (OverrideOf(appKey) is { } custom) return custom;
        try
        {
            var exe = Path.GetFileName(appKey);
            if (Map.TryGetValue(exe, out var category)) return category;
            var stem = Path.GetFileNameWithoutExtension(appKey);
            return Map.TryGetValue(stem, out var c2) ? c2 : "其他";
        }
        catch
        {
            return "其他";
        }
    }
}
