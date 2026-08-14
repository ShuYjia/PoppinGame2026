---
name: TMPChineseFont
description: Silently prepares TextMeshPro Essential Resources and configures the bundled Noto Sans Simplified Chinese fallback font with explicit user consent. Use before creating or modifying TMP content, and whenever Chinese TMP text is missing, blank, tofu, square, or garbled.
allowedTools:
  - ask_user
  - unity_editor
  - unity_package
  - unity_refresh
  - execute_csharp_script
---

# TMP Chinese font

Use this skill whenever a Unity task introduces Chinese text through
TextMeshPro. Do not modify font assets or global TMP settings before the user
authorizes the change.

## 1. Prepare TMP Essentials

Before any operation creates or accesses `TMP_Text`, `TextMeshPro`,
`TextMeshProUGUI`, or `TMP_InputField`, pass the following block verbatim as
the inline `script` value of `execute_csharp_script` in Editor mode. Do not
open the TMP Importer menu and do not write this block to a file.

```csharp
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

const string SettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
{
    var windowType = assembly.GetType(
        "TMPro.TMP_PackageResourceImporterWindow",
        false);
    if (windowType == null)
        continue;
    foreach (var item in Resources.FindObjectsOfTypeAll(windowType))
        if (item is EditorWindow window)
            window.Close();
}

if (AssetDatabase.LoadMainAssetAtPath(SettingsPath) != null)
    return "TMP_ESSENTIALS_READY imported=false";

var packageInfo =
    UnityEditor.PackageManager.PackageInfo.FindForAssetPath(
        "Packages/com.unity.textmeshpro/package.json");
if (packageInfo == null || string.IsNullOrEmpty(packageInfo.resolvedPath))
    return "TMP_PACKAGE_MISSING";

var packagePath = Path.Combine(
    packageInfo.resolvedPath,
    "Package Resources",
    "TMP Essential Resources.unitypackage");
if (!File.Exists(packagePath))
    return $"TMP_ESSENTIALS_PACKAGE_MISSING path={packagePath}";

AssetDatabase.ImportPackage(packagePath, false);
return "TMP_ESSENTIALS_IMPORT_STARTED";
```

When the result is `TMP_ESSENTIALS_IMPORT_STARTED`, call `unity_refresh` and
then run this verification script:

```csharp
using UnityEditor;

const string SettingsPath =
    "Assets/TextMesh Pro/Resources/TMP Settings.asset";

return AssetDatabase.LoadMainAssetAtPath(SettingsPath) != null
    ? "TMP_ESSENTIALS_READY imported=true"
    : "TMP_ESSENTIALS_IMPORT_PENDING";
```

If verification reports `TMP_ESSENTIALS_IMPORT_PENDING`, call `unity_refresh`
once more and repeat only the verification script. If it is still pending,
report `TMP_ESSENTIALS_IMPORT_FAILED` and stop. Do not improvise another C#
waiting or main-thread script, do not call
`EditorApplication.ExecuteActionOnMainThread` (that API does not exist), and
do not open the TMP Importer menu.

Continue only after `TMP_ESSENTIALS_READY`. If the package is missing, ask
before installing `com.unity.textmeshpro`, wait for Unity compilation, and
repeat the preparation script. This preparation is authorized by a request
involving TMP, but it must not configure a Chinese fallback by itself.

## 2. Inspect Chinese fallback support

For Chinese content, run this inline Editor script after preparation:

```csharp
using System.Linq;
using TMPro;
using UnityEditor;

const string FontAssetPath =
    "Assets/Codely/Fonts/NotoSansSC-Regular SDF.asset";

var settings = TMP_Settings.instance;
if (settings == null)
    return "TMP_SETTINGS_MISSING";

var expected = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
var fallbacks = TMP_Settings.fallbackFontAssets;
var hasChineseFallback = fallbacks != null && fallbacks.Any(
    font => font != null && font.HasCharacter('中', true, false));

return hasChineseFallback
    ? "TMP_CHINESE_FONT_READY"
    : $"TMP_CHINESE_FONT_SETUP_REQUIRED expectedAssetExists={expected != null}";
```

If this reports `TMP_CHINESE_FONT_READY`, make no font-setting changes.

## 3. Obtain authorization and detect conflicts

Before setup, tell the user that it can create:

- `Assets/Codely/Fonts/NotoSansSC-Regular.otf`
- `Assets/Codely/Fonts/NotoSansSC-OFL.txt`
- `Assets/Codely/Fonts/NotoSansSC-Regular SDF.asset`
- one entry in the global TMP fallback list

Use `ask_user` to obtain explicit authorization. Then pass the Editor script
below to `execute_csharp_script`. Replace `<skill-base>` with the absolute path
of this skill directory while preserving the verbatim string syntax (`@"..."`).
This inspection script does not modify files.

```csharp
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

const string SkillBase = @"<skill-base>";

var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
if (string.IsNullOrEmpty(projectRoot))
    return "UNITY_PROJECT_ROOT_MISSING";

var pairs = new[]
{
    new
    {
        Source = Path.Combine(
            SkillBase, "assets", "NotoSansSC-Regular.otf"),
        Target = Path.Combine(
            projectRoot, "Assets", "Codely", "Fonts",
            "NotoSansSC-Regular.otf")
    },
    new
    {
        Source = Path.Combine(SkillBase, "assets", "OFL.txt"),
        Target = Path.Combine(
            projectRoot, "Assets", "Codely", "Fonts",
            "NotoSansSC-OFL.txt")
    }
};

var result = new StringBuilder();
foreach (var pair in pairs)
{
    string status;
    if (!File.Exists(pair.Source))
    {
        status = $"BUNDLED_SOURCE_MISSING {pair.Source}";
    }
    else if (!File.Exists(pair.Target))
    {
        status = $"TARGET_ABSENT_WILL_CREATE {pair.Target}";
    }
    else
    {
        string sourceHash;
        string targetHash;
        using (var sha256 = SHA256.Create())
        using (var stream = File.OpenRead(pair.Source))
            sourceHash = Convert.ToBase64String(
                sha256.ComputeHash(stream));
        using (var sha256 = SHA256.Create())
        using (var stream = File.OpenRead(pair.Target))
            targetHash = Convert.ToBase64String(
                sha256.ComputeHash(stream));
        status = sourceHash == targetHash
            ? $"MATCH {pair.Target}"
            : $"CONFLICT {pair.Target}";
    }

    if (result.Length > 0)
        result.AppendLine();
    result.Append(status);
}

return result.ToString();
```

`TARGET_ABSENT_WILL_CREATE` is the normal first-run state, not an error.
`BUNDLED_SOURCE_MISSING` means the Extension package is incomplete; stop
without changing the project.

On any `CONFLICT`, show the paths and ask the user to choose `reuse`,
`replace`, or `cancel`. Replacing requires a separate explicit confirmation.
Never silently overwrite a conflicting file.

After authorization, handle the inspection result as follows:

- No conflict: run the copy script with `ReplaceExisting = false`.
- `replace`: after the separate confirmation, run it with
  `ReplaceExisting = true`.
- `reuse`: skip the copy script; the existing project font is validated by the
  configuration script.

For copy or replacement, pass this block to `execute_csharp_script`. Replace
`<skill-base>` as described above and set `ReplaceExisting` according to the
chosen action.

```csharp
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

const string SkillBase = @"<skill-base>";
const bool ReplaceExisting = false;

if (EditorApplication.isPlayingOrWillChangePlaymode)
    throw new InvalidOperationException("TMP_FONT_REQUIRES_EDIT_MODE");

var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
if (string.IsNullOrEmpty(projectRoot))
    return "UNITY_PROJECT_ROOT_MISSING";

var targetDirectory = Path.Combine(
    projectRoot, "Assets", "Codely", "Fonts");
var fontSource = Path.Combine(
    SkillBase, "assets", "NotoSansSC-Regular.otf");
var licenseSource = Path.Combine(SkillBase, "assets", "OFL.txt");
var fontTarget = Path.Combine(
    targetDirectory, "NotoSansSC-Regular.otf");
var licenseTarget = Path.Combine(
    targetDirectory, "NotoSansSC-OFL.txt");

if (!File.Exists(fontSource))
    return $"BUNDLED_SOURCE_MISSING {fontSource}";
if (!File.Exists(licenseSource))
    return $"BUNDLED_SOURCE_MISSING {licenseSource}";

Directory.CreateDirectory(targetDirectory);
if (ReplaceExisting || !File.Exists(fontTarget))
    File.Copy(fontSource, fontTarget, ReplaceExisting);
if (ReplaceExisting || !File.Exists(licenseTarget))
    File.Copy(licenseSource, licenseTarget, ReplaceExisting);

AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
return ReplaceExisting
    ? "FONT_FILES_REPLACED"
    : "FONT_FILES_READY";
```

## 4. Configure the fallback

After the copy script succeeds, pass this block verbatim as the inline Editor
script:

```csharp
using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

const string SourceFontPath =
    "Assets/Codely/Fonts/NotoSansSC-Regular.otf";
const string FontAssetPath =
    "Assets/Codely/Fonts/NotoSansSC-Regular SDF.asset";

if (EditorApplication.isPlayingOrWillChangePlaymode)
    throw new InvalidOperationException("TMP_FONT_REQUIRES_EDIT_MODE");

var settings = TMP_Settings.instance;
if (settings == null)
    return "TMP_SETTINGS_MISSING";

AssetDatabase.ImportAsset(
    SourceFontPath,
    ImportAssetOptions.ForceSynchronousImport);
var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
if (sourceFont == null)
    return $"TMP_SOURCE_FONT_MISSING path={SourceFontPath}";
if (!sourceFont.HasCharacter('中'))
    return $"EXISTING_FONT_UNSUPPORTED path={SourceFontPath}";

var existingObject = AssetDatabase.LoadMainAssetAtPath(FontAssetPath);
var fontAsset = existingObject as TMP_FontAsset;
if (existingObject != null && fontAsset == null)
    return $"FONT_ASSET_PATH_CONFLICT path={FontAssetPath} type={existingObject.GetType().FullName}";

var created = false;
if (fontAsset == null)
{
    fontAsset = TMP_FontAsset.CreateFontAsset(
        sourceFont,
        90,
        9,
        GlyphRenderMode.SDFAA,
        2048,
        2048,
        AtlasPopulationMode.Dynamic,
        true);
    if (fontAsset == null)
        return "TMP_FONT_CREATION_FAILED";

    fontAsset.name = "NotoSansSC-Regular SDF";
    fontAsset.isMultiAtlasTexturesEnabled = true;
    AssetDatabase.CreateAsset(fontAsset, FontAssetPath);

    if (fontAsset.material != null &&
        !EditorUtility.IsPersistent(fontAsset.material))
    {
        fontAsset.material.name =
            "NotoSansSC-Regular Atlas Material";
        AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
    }

    foreach (var atlas in fontAsset.atlasTextures ??
             Array.Empty<Texture2D>())
    {
        if (atlas == null || EditorUtility.IsPersistent(atlas))
            continue;
        atlas.name = "NotoSansSC-Regular Atlas";
        AssetDatabase.AddObjectToAsset(atlas, fontAsset);
    }
    created = true;
}

fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
fontAsset.isMultiAtlasTexturesEnabled = true;

var fallbacks = TMP_Settings.fallbackFontAssets;
if (fallbacks == null)
    return "TMP_FALLBACK_LIST_MISSING";

var fallbackAdded = false;
if (!fallbacks.Contains(fontAsset))
{
    fallbacks.Add(fontAsset);
    fallbackAdded = true;
}

EditorUtility.SetDirty(fontAsset);
EditorUtility.SetDirty(settings);
AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

return
    $"TMP_CHINESE_FONT_READY created={created} " +
    $"fallbackAdded={fallbackAdded} fontAsset={FontAssetPath}";
```

Treat only `TMP_CHINESE_FONT_READY` as success. Report any other status
exactly. Importing TMP Essentials alone does not add Chinese glyphs.
