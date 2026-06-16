using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class WebBuildCli
{
    private const string CanvasMarkup = "<canvas id=\"unity-canvas\" width=960 height=600 tabindex=\"-1\"></canvas>";
    private const string FocusableCanvasMarkup = "<canvas id=\"unity-canvas\" width=960 height=600 tabindex=\"0\"></canvas>";
    private const string CanvasQuery = "      var canvas = document.querySelector(\"#unity-canvas\");";
    private const string PointerFocusScript = "      canvas.addEventListener(\"pointerdown\", () => canvas.focus());";

    public static void BuildWebGL()
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
            ?? throw new InvalidOperationException("Could not resolve project root.");

        string outputPath = Path.Combine(projectRoot, "Web");
        if (!Directory.Exists(outputPath))
            Directory.CreateDirectory(outputPath);

        string[] enabledScenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (enabledScenes.Length == 0)
            throw new InvalidOperationException("No enabled scenes in Build Settings.");

        var buildOptions = new BuildPlayerOptions
        {
            scenes = enabledScenes,
            locationPathName = outputPath,
            target = BuildTarget.WebGL,
            options = BuildOptions.CleanBuildCache
        };

        BuildReport report = BuildPipeline.BuildPlayer(buildOptions);
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"WebGL build failed: {report.summary.result}");

        ApplyWebKeyboardFocus(outputPath);
    }

    private static void ApplyWebKeyboardFocus(string outputPath)
    {
        string indexPath = Path.Combine(outputPath, "index.html");
        if (!File.Exists(indexPath))
            throw new FileNotFoundException("WebGL index.html was not generated.", indexPath);

        string html = File.ReadAllText(indexPath);
        html = html.Replace(CanvasMarkup, FocusableCanvasMarkup);

        if (!html.Contains(PointerFocusScript))
            html = html.Replace(CanvasQuery, CanvasQuery + Environment.NewLine + PointerFocusScript);

        File.WriteAllText(indexPath, html);
    }
}
