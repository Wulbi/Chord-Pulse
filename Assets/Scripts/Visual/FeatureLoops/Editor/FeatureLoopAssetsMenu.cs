using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Saves the built-in look (<see cref="FeatureLoopLibrary.CreateDefault"/>) as editable assets:
/// Resources/FeatureLoopLibrary plus its styles and one <see cref="FeatureLoopData"/> per generated loop.
/// Run again after adding loops or styles: it only fills in what is missing (instruments without a style,
/// loops without data, loop data without overrides) and never overwrites values that were edited.
/// </summary>
static class FeatureLoopAssetsMenu
{
    const string ResourcesFolder = "Assets/Resources";
    const string LoopsFolder = ResourcesFolder + "/FeatureLoops";

    [MenuItem("Chord Pulse/Create or Update Feature Loop Assets")]
    static void CreateOrUpdate()
    {
        if (!AssetDatabase.IsValidFolder(ResourcesFolder))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(LoopsFolder))
            AssetDatabase.CreateFolder(ResourcesFolder, "FeatureLoops");

        var defaults = FeatureLoopLibrary.CreateDefault();
        var saved = new HashSet<Object>();
        int changes = 0;

        string libraryPath = $"{ResourcesFolder}/{FeatureLoopLibrary.ResourcePath}.asset";
        var library = AssetDatabase.LoadAssetAtPath<FeatureLoopLibrary>(libraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<FeatureLoopLibrary>();
            AssetDatabase.CreateAsset(library, libraryPath);
        }

        if (library.fallbackStyle == null)
        {
            library.fallbackStyle = Persist(defaults.fallbackStyle, saved);
            changes++;
        }

        if (library.feverStyle == null && defaults.feverStyle != null)
        {
            library.feverStyle = Persist(defaults.feverStyle, saved);
            changes++;
        }

        var voices = library.voiceDefaults.ToList();
        foreach (var voice in defaults.voiceDefaults)
            if (!voices.Any(v => v.instrument == voice.instrument && v.style != null))
            {
                voices.RemoveAll(v => v.instrument == voice.instrument);
                voices.Add(new FeatureLoopLibrary.VoiceDefault { instrument = voice.instrument, style = Persist(voice.style, saved) });
                changes++;
            }
        library.voiceDefaults = voices.ToArray();

        var loops = library.loops.Where(l => l != null).ToList();
        foreach (var defaultData in defaults.loops)
        {
            var data = loops.FirstOrDefault(l => l.loopId == defaultData.loopId);
            if (data == null)
            {
                data = Persist(defaultData, saved);
                loops.Add(data);
                changes++;
            }
            else if (data.voices.Length == 0 && defaultData.voices.Length > 0)
            {
                data.voices = defaultData.voices;
                changes++;
            }
            // Overrides may point at styles that only exist in memory so far.
            for (int i = 0; i < data.voices.Length; i++)
                if (data.voices[i].style != null && !EditorUtility.IsPersistent(data.voices[i].style))
                    data.voices[i].style = Persist(data.voices[i].style, saved);
            EditorUtility.SetDirty(data);
        }
        library.loops = loops.OrderBy(l => l.loopId).ToArray();

        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
        DestroyUnsaved(defaults, saved);
        Selection.activeObject = library;
        Debug.Log($"[FeatureLoopAssets] {libraryPath}: {library.loops.Length} loops, {library.voiceDefaults.Length} instrument styles ({changes} changes).", library);
    }

    /// <summary>Saves an in-memory default under LoopsFolder, or returns the asset already saved under its name.</summary>
    static T Persist<T>(T temp, HashSet<Object> saved) where T : ScriptableObject
    {
        string path = $"{LoopsFolder}/{temp.name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null)
            return existing;
        temp.hideFlags = HideFlags.None;
        AssetDatabase.CreateAsset(temp, path);
        saved.Add(temp);
        return temp;
    }

    static void DestroyUnsaved(FeatureLoopLibrary defaults, HashSet<Object> saved)
    {
        var temps = new HashSet<Object> { defaults, defaults.fallbackStyle, defaults.feverStyle };
        foreach (var voice in defaults.voiceDefaults)
            temps.Add(voice.style);
        foreach (var data in defaults.loops)
        {
            temps.Add(data);
            foreach (var voice in data.voices)
                temps.Add(voice.style);
        }
        foreach (var temp in temps)
            if (temp != null && !saved.Contains(temp) && !EditorUtility.IsPersistent(temp))
                Object.DestroyImmediate(temp);
    }
}
