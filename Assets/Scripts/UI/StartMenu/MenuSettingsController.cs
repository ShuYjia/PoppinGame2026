using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 主菜单基础设置：音量、分辨率、全屏。
/// 只使用独立的 PlayerPrefs 键，不写入未来的肉鸽大存档。
/// </summary>
public sealed class MenuSettingsController : MonoBehaviour
{
    public const string VolumeKey = "Poppin.Menu.Volume";
    public const string FullscreenKey = "Poppin.Menu.Fullscreen";
    public const string ResolutionKey = "Poppin.Menu.Resolution";

    private Slider volumeSlider;
    private TMP_Dropdown resolutionDropdown;
    private Toggle fullscreenToggle;

    private readonly List<Vector2Int> resolutions = new List<Vector2Int>();
    private bool configured;

    /// <summary>由 StartMenuShell 创建控件后调用一次。</summary>
    public void Configure(Slider volume, TMP_Dropdown resolution, Toggle fullscreen)
    {
        volumeSlider = volume;
        resolutionDropdown = resolution;
        fullscreenToggle = fullscreen;

        if (configured)
            return;

        configured = true;
        BuildResolutionOptions();
        LoadSettings();
    }

    /// <summary>应用当前控件值，并写入独立设置键。</summary>
    public void Apply()
    {
        float volume = volumeSlider != null ? Mathf.Clamp01(volumeSlider.value) : PlayerPrefs.GetFloat(VolumeKey, 1f);
        bool fullscreen = fullscreenToggle != null ? fullscreenToggle.isOn : PlayerPrefs.GetInt(FullscreenKey, 1) == 1;
        Vector2Int resolution = GetSelectedResolution();

        AudioListener.volume = volume;
        Screen.SetResolution(resolution.x, resolution.y, fullscreen);

        PlayerPrefs.SetFloat(VolumeKey, volume);
        PlayerPrefs.SetInt(FullscreenKey, fullscreen ? 1 : 0);
        PlayerPrefs.SetString(ResolutionKey, resolution.x + "x" + resolution.y);
        PlayerPrefs.Save();

        Debug.Log("[MenuSettings] 已应用设置：" + resolution.x + "x" + resolution.y + " fullscreen=" + fullscreen + " volume=" + volume);
    }

    /// <summary>恢复默认值：音量 100%、全屏开启、当前分辨率。</summary>
    public void RestoreDefaults()
    {
        if (volumeSlider != null)
            volumeSlider.SetValueWithoutNotify(1f);
        if (fullscreenToggle != null)
            fullscreenToggle.SetIsOnWithoutNotify(true);

        int currentIndex = FindResolutionIndex(Screen.width + "x" + Screen.height);
        if (resolutionDropdown != null && currentIndex >= 0)
            resolutionDropdown.SetValueWithoutNotify(currentIndex);

        Apply();
    }

    private void BuildResolutionOptions()
    {
        resolutions.Clear();
        if (resolutionDropdown == null)
            return;

        List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();
        HashSet<string> seen = new HashSet<string>();

        Resolution[] available = Screen.resolutions;
        for (int i = 0; i < available.Length; i++)
        {
            string key = available[i].width + "x" + available[i].height;
            if (!seen.Add(key))
                continue;

            resolutions.Add(new Vector2Int(available[i].width, available[i].height));
            options.Add(new TMP_Dropdown.OptionData(available[i].width + " × " + available[i].height));
        }

        if (options.Count == 0)
        {
            resolutions.Add(new Vector2Int(Screen.width, Screen.height));
            options.Add(new TMP_Dropdown.OptionData(Screen.width + " × " + Screen.height));
        }

        resolutionDropdown.ClearOptions();
        resolutionDropdown.AddOptions(options);
    }

    private void LoadSettings()
    {
        float volume = PlayerPrefs.GetFloat(VolumeKey, 1f);
        bool fullscreen = PlayerPrefs.GetInt(FullscreenKey, Screen.fullScreen ? 1 : 0) == 1;
        string savedResolution = PlayerPrefs.GetString(ResolutionKey, Screen.width + "x" + Screen.height);

        if (volumeSlider != null)
            volumeSlider.SetValueWithoutNotify(Mathf.Clamp01(volume));
        if (fullscreenToggle != null)
            fullscreenToggle.SetIsOnWithoutNotify(fullscreen);

        int index = FindResolutionIndex(savedResolution);
        if (index < 0)
            index = FindResolutionIndex(Screen.width + "x" + Screen.height);
        if (index < 0)
            index = 0;
        if (resolutionDropdown != null)
            resolutionDropdown.SetValueWithoutNotify(index);

        AudioListener.volume = Mathf.Clamp01(volume);
    }

    private Vector2Int GetSelectedResolution()
    {
        if (resolutionDropdown == null || resolutions.Count == 0)
            return new Vector2Int(Screen.width, Screen.height);

        int index = Mathf.Clamp(resolutionDropdown.value, 0, resolutions.Count - 1);
        return resolutions[index];
    }

    private int FindResolutionIndex(string key)
    {
        for (int i = 0; i < resolutions.Count; i++)
        {
            if (resolutions[i].x + "x" + resolutions[i].y == key)
                return i;
        }

        return -1;
    }
}
