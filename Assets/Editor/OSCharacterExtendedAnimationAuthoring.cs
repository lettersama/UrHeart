using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public static class OSCharacterExtendedAnimationAuthoring
{
    const string ControllerPath = "Assets/Animator/UI/Player/Player_UI_Controller.controller";
    const string ClipFolder = "Assets/Animation/UI/Player/";
    const string StatePrefix = "Player_UI_State_";
    const float SampleRate = 10;
    static readonly string[] Names = {
        "Expend00", "Expend01", "Expend02", "OpenLive", "Nervous", "Sad",
        "Breakdown00", "Breakdown01", "Breakdown02", "Angry00", "Angry01",
        "MagnifyingGlass00", "MagnifyingGlass01"
    };

    [MenuItem("Tools/MrTGame/Character/Build Extended Animations")]
    static void BuildMenu() => Debug.Log(Build());

    public static string Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Build character animations in Edit Mode.");
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null || controller.layers.Length == 0)
            throw new InvalidOperationException("Character Animator Controller is missing.");
        var machine = controller.layers[0].stateMachine;
        var normal = machine.states.FirstOrDefault(child => child.state.name == StatePrefix + "Normal").state;
        if (normal == null)
            throw new InvalidOperationException("Character Normal state is missing.");

        ImportEmotionalSheets();

        // Reuse the artist's imported slices and their left-to-right frame order.
        var frames = Names.Select(name => AssetDatabase.LoadAllAssetsAtPath(
                "Assets/Art/Animator/StatusAnimations_" + name + ".png")
            .OfType<Sprite>().OrderByDescending(sprite => sprite.rect.y)
            .ThenBy(sprite => sprite.rect.x).ToArray()).ToArray();
        for (int i = 0; i < Names.Length; i++)
        {
            int expected = Names[i] == "OpenLive" ? 1 : Names[i] == "MagnifyingGlass01" ? 16 : 8;
            if (frames[i].Length != expected)
                throw new InvalidOperationException($"{Names[i]} needs {expected} imported sprites; found {frames[i].Length}.");
            var parameter = controller.parameters.FirstOrDefault(value => value.name == Names[i]);
            if (parameter != null && parameter.type != AnimatorControllerParameterType.Bool)
                throw new InvalidOperationException("Expected Bool parameter: " + Names[i]);
        }

        for (int i = 0; i < Names.Length; i++)
        {
            string stateName = StatePrefix + Names[i];
            string path = ClipFolder + stateName + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip { name = stateName };
                AssetDatabase.CreateAsset(clip, path);
            }
            clip.frameRate = SampleRate;
            clip.legacy = false;
            var binding = EditorCurveBinding.PPtrCurve(string.Empty, typeof(UnityEngine.UI.Image), "m_Sprite");
            var keys = frames[i].Select((sprite, frame) => new ObjectReferenceKeyframe
                { time = frame / SampleRate, value = sprite }).ToArray();
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.startTime = 0;
            settings.stopTime = frames[i].Length / SampleRate;
            settings.loopTime = Names[i] != "Expend00" && Names[i] != "MagnifyingGlass00";
            settings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);

            if (!controller.parameters.Any(parameter => parameter.name == Names[i]))
                controller.AddParameter(Names[i], AnimatorControllerParameterType.Bool);
            var state = machine.states.FirstOrDefault(child => child.state.name == stateName).state
                ?? machine.AddState(stateName, new Vector3(-340, 300 + i * 85, 0));
            state.motion = clip;
            state.speed = 1;
            state.writeDefaultValues = normal.writeDefaultValues;

            string entryName = "Extended_" + Names[i];
            var entry = machine.anyStateTransitions.FirstOrDefault(transition => transition.name == entryName)
                ?? machine.AddAnyStateTransition(state);
            entry.name = entryName;
            entry.destinationState = state;
            ConfigureTransition(entry);
            ClearConditions(entry);
            entry.AddCondition(AnimatorConditionMode.If, 0, Names[i]);
            // API calls are exclusive; priority also makes manual multi-switch edits stable.
            for (int previous = 0; previous < i; previous++)
                entry.AddCondition(AnimatorConditionMode.IfNot, 0, Names[previous]);

            var exit = state.transitions.FirstOrDefault(transition => transition.name == "Extended_Release")
                ?? state.AddTransition(normal);
            exit.name = "Extended_Release";
            exit.destinationState = normal;
            ConfigureTransition(exit);
            ClearConditions(exit);
            foreach (string parameter in Names)
                exit.AddCondition(AnimatorConditionMode.IfNot, 0, parameter);
            EditorUtility.SetDirty(state);
            EditorUtility.SetDirty(entry);
            EditorUtility.SetDirty(exit);
            AssetDatabase.SaveAssetIfDirty(clip);
        }
        EditorUtility.SetDirty(machine);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssetIfDirty(controller);
        return "Updated 13 character clips at 10 FPS with exclusive Bool switches and Any State transitions. Expend00 and MagnifyingGlass00 play once; all other clips loop.";
    }

    static void ImportEmotionalSheets()
    {
        var factories = new SpriteDataProviderFactories();
        factories.Init();
        // The four earlier exports retain their artist-authored slicing and IDs.
        // New emotional strips use the same untrimmed 125 x 175 canvas and PPU.
        foreach (string name in Names.Skip(4))
        {
            string path = "Assets/Art/Animator/StatusAnimations_" + name + ".png";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("Missing emotional animation strip: " + path);
            int count = name == "MagnifyingGlass01" ? 16 : 8;
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            if (width != 125 * count || height != 175)
                throw new InvalidOperationException($"{name}: expected {125 * count} x 175, found {width} x {height}.");
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = 20;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;
            importer.alphaIsTransparency = true;
            var textureSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(textureSettings);
            textureSettings.spriteMeshType = SpriteMeshType.FullRect;
            textureSettings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(textureSettings);

            var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            var previous = provider.GetSpriteRects();
            var slices = new SpriteRect[count];
            for (int frame = 0; frame < count; frame++)
            {
                string spriteName = name + "_" + frame.ToString("00");
                var existing = previous.FirstOrDefault(rect => rect.name == spriteName);
                slices[frame] = new SpriteRect {
                    name = spriteName,
                    spriteID = existing != null ? existing.spriteID : GUID.Generate(),
                    rect = new Rect(frame * 125, 0, 125, 175),
                    alignment = SpriteAlignment.Center,
                    pivot = new Vector2(.5f, .5f)
                };
            }
            provider.SetSpriteRects(slices);
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>()
                .SetNameFileIdPairs(slices.Select(rect => new SpriteNameFileIdPair(rect.name, rect.spriteID)));
            provider.Apply();
            importer.SaveAndReimport();
        }
    }

    static void ConfigureTransition(AnimatorStateTransition transition)
    {
        transition.hasExitTime = false;
        transition.hasFixedDuration = true;
        transition.duration = 0;
        transition.offset = 0;
        transition.canTransitionToSelf = false;
        transition.interruptionSource = TransitionInterruptionSource.None;
    }

    static void ClearConditions(AnimatorStateTransition transition)
    {
        foreach (var condition in transition.conditions)
            transition.RemoveCondition(condition);
    }
}
